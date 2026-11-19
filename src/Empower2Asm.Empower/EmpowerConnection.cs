using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using MillenniumToolkit;

namespace Empower2Asm.Empower
{
    /// <summary>
    /// One connection to an Empower database through the Toolkit. Every Empower read happens inside one Project, so the
    /// connection logs in to one Project at a time and switches when asked for another. Read-only: nothing here, nor
    /// in the classes that use it, calls a Toolkit <c>Set</c>, <c>Update</c> or processing method.
    /// </summary>
    public sealed class EmpowerConnection : IDisposable
    {
        private readonly ToolkitThread _thread = new ToolkitThread();
        private readonly EmpowerLogin _login;
        private Project? _project;
        private string? _currentProject;

        /// <summary>Prepares a connection; nothing is sent to Empower until the first call.</summary>
        public EmpowerConnection(EmpowerLogin login)
        {
            _login = login;
        }

        /// <summary>The Empower database this connection reads from.</summary>
        public string Database => _login.Database;

        /// <summary>The Projects the user can see, from a login that names no Project.</summary>
        public IReadOnlyList<string> ListProjects() => _thread.Run(() =>
        {
            LoginTo("");
            try
            {
                var names = ((object)_project!.Names as Array)?.Cast<object>().Select(n => n.ToString()).ToList()
                    ?? new List<string>();
                names.Sort(StringComparer.Ordinal);
                return (IReadOnlyList<string>)names;
            }
            catch (COMException e)
            {
                throw new EmpowerException($"Listing the Projects failed: {Describe(_project!, e)}", e);
            }
        });

        /// <summary>
        /// Runs Toolkit work inside one Project, on the Toolkit thread. The work must not let Toolkit objects escape.
        /// </summary>
        internal T InProject<T>(string project, Func<T> work) => _thread.Run(() =>
        {
            if (project.Length == 0)
            {
                throw new ArgumentException("A Project name is required.", nameof(project));
            }

            LoginTo(project);
            try
            {
                return work();
            }
            catch (COMException e)
            {
                throw new EmpowerException($"Reading Project '{project}' failed: {Describe(_project!, e)}", e);
            }
        });

        private void LoginTo(string project)
        {
            if (_project != null && _currentProject == project)
            {
                return;
            }

            Logoff();
            var p = new Project();
            try
            {
                p.Login(_login.Database, project, _login.User, _login.Password);
                if (project.Length > 0)
                {
                    // Binds this thread to the login. A partial login (no Project) only allows Names and rejects
                    // this call with "Login required".
                    p.SetCurrentUser();
                }
            }
            catch (COMException e)
            {
                // Login may have succeeded before SetCurrentUser failed: don't leave that session open.
                TryLogoff(p);
                throw new EmpowerException(
                    $"Login to Empower as {_login} failed{(project.Length == 0 ? "" : $" for Project '{project}'")}: {Describe(p, e)}", e);
            }

            _project = p;
            _currentProject = project;
        }

        /// <summary>
        /// Logs off, ignoring a failure: it happens on a dead session, and must not hide the error that led here.
        /// </summary>
        private static void TryLogoff(Project project)
        {
            try
            {
                project.Logoff();
            }
            catch (COMException)
            {
                // The session is gone either way.
            }
        }

        /// <summary>The Toolkit's own text for a COM error, e.g. 0x80040207 "Invalid User/Password".</summary>
        private static string Describe(Project project, COMException error)
        {
            try
            {
                var text = project.TkErrorDescription(error.ErrorCode);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return $"{text.Trim()} (0x{error.ErrorCode:X8})";
                }
            }
            catch (COMException)
            {
                // Fall back to the COM message.
            }

            return error.Message;
        }

        private void Logoff()
        {
            if (_project == null)
            {
                return;
            }

            var project = _project;
            _project = null;
            _currentProject = null;
            TryLogoff(project);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            try
            {
                _thread.Run(() =>
                {
                    Logoff();
                    return 0;
                });
            }
            finally
            {
                _thread.Dispose();
            }
        }
    }
}
