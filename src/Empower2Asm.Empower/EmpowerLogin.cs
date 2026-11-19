namespace Empower2Asm.Empower
{
    /// <summary>The credentials used to log in to one Empower database.</summary>
    public sealed class EmpowerLogin
    {
        /// <summary>Creates a login.</summary>
        /// <param name="database">The Empower database (Oracle service) name; empty for the local service.</param>
        /// <param name="user">The Empower user name.</param>
        /// <param name="password">The Empower password.</param>
        public EmpowerLogin(string database, string user, string password)
        {
            Database = database;
            User = user;
            Password = password;
        }

        /// <summary>The Empower database (Oracle service) name; empty for the local service.</summary>
        public string Database { get; }

        /// <summary>The Empower user name.</summary>
        public string User { get; }

        /// <summary>The Empower password.</summary>
        public string Password { get; }

        /// <summary>Keeps the password out of logs and exception messages.</summary>
        public override string ToString() => $"{User}@{(Database.Length == 0 ? "(local)" : Database)}";
    }
}
