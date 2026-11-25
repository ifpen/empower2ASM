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
        private bool _disposed;

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
            catch (Exception e) when (!(e is EmpowerException))
            {
                if (Dropped("Listing the Projects failed", e) is EmpowerException error)
                {
                    throw error;
                }

                throw;
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
            catch (Exception e) when (!(e is EmpowerException))
            {
                if (Dropped($"Reading Project '{project}' failed", e) is EmpowerException error)
                {
                    throw error;
                }

                throw;
            }
        });

        /// <summary>
        /// Ends the session after an error in Toolkit work, so the next call logs in again. The session may be dead
        /// (network drop, server restart, timeout), which shows as a COMException or as another error (a released COM
        /// object, a missing array), and nothing tells that apart from other errors; a new login is cheap, while
        /// keeping a dead session would fail every remaining Injection of the Project. Only an EmpowerException, the
        /// adapter's own verdict on the data, keeps the session. Returns a Toolkit error as an EmpowerException with
        /// the Toolkit's own text; null for any other error, to rethrow as it is.
        /// </summary>
        private EmpowerException? Dropped(string what, Exception error)
        {
            var wrapped = error is COMException com ? new EmpowerException($"{what}: {Describe(_project!, com)}", com) : null;
            Logoff();
            return wrapped;
        }

        private void LoginTo(string project)
        {
            if (_project != null && _currentProject == project)
            {
                return;
            }

            Logoff();
            var p = new Project();
            var loggedIn = false;
            try
            {
                p.Login(_login.Database, project, _login.User, _login.Password);
                loggedIn = true;
                if (project.Length > 0)
                {
                    // Binds this thread to the login. A partial login (no Project) only allows Names and rejects
                    // this call with "Login required".
                    p.SetCurrentUser();
                }
            }
            catch (COMException e)
            {
                var message = $"Login to Empower as {_login} failed{(project.Length == 0 ? "" : $" for Project '{project}'")}: {Describe(p, e)}";
                // Login succeeded if only SetCurrentUser failed: don't leave that session open. After a failed Login,
                // Logoff crashes the process inside the Toolkit (access violation), so it is never called then.
                if (loggedIn)
                {
                    TryLogoff(p);
                }

                throw new EmpowerException(message, e);
            }

            _project = p;
            _currentProject = project;
        }

        /// <summary>
        /// Logs off, ignoring a failure: it happens on a dead session, and must not hide the error that led here. Still
        /// called on a session that may be dead, so that a live one doesn't hold its licence until the server drops it.
        /// </summary>
        private static void TryLogoff(Project project)
        {
            try
            {
                project.Logoff();
            }
            catch (Exception)
            {
                // The session is gone either way; a dead one may fail with other than a COMException.
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
            catch (Exception)
            {
                // Fall back to the COM message, also when the session is too dead to describe its error.
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
            if (_disposed)
            {
                return;
            }

            _disposed = true;
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
