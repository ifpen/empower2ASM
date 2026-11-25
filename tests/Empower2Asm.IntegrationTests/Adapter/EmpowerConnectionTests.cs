using System;
using Empower2Asm.Core.Source;
using Empower2Asm.Empower;
using Xunit;

namespace Empower2Asm.IntegrationTests.Adapter
{
    /// <summary>What a connection does before any login: nothing here reaches Empower.</summary>
    public sealed class EmpowerConnectionTests
    {
        private static EmpowerConnection Connection() => new EmpowerConnection(new EmpowerLogin("EMPOWER", "user", "password"));

        [Fact]
        public void Can_be_disposed_twice()
        {
            var connection = Connection();

            connection.Dispose();
            connection.Dispose();
        }

        [Fact]
        public void Refuses_work_once_disposed()
        {
            var connection = Connection();
            connection.Dispose();

            Assert.Throws<ObjectDisposedException>(() => connection.ListProjects());
        }

        [Fact]
        public void Takes_the_database_name_in_any_case()
        {
            using var connection = Connection();
            var source = new EmpowerSource(connection);

            // The key is accepted, so the call goes on to log in, which the disposed connection refuses.
            connection.Dispose();
            Assert.Throws<ObjectDisposedException>(() => source.LoadInjection(new InjectionKey("empower", "Project", 1)));
            Assert.Throws<ArgumentException>(() => source.LoadInjection(new InjectionKey("OTHER", "Project", 1)));
        }
    }
}
