namespace Empower2Asm.Core.Source
{
    /// <summary>
    /// Identifies one Injection across Empower Databases and Projects. Empower Injection IDs are only unique within
    /// one Project of one Empower Database, so all three parts are needed.
    /// </summary>
    /// <param name="Database">The Empower Database, by the Oracle service name the login used.</param>
    /// <param name="Project">The Empower Project name, as Empower spells it.</param>
    /// <param name="InjectionId">The Empower Injection ID, unique within the Project.</param>
    public sealed record InjectionKey(string Database, string Project, int InjectionId)
    {
        private readonly string _database = Upper(Database);

        /// <summary>
        /// The Empower Database, by its Oracle service name in upper case: service names ignore case, so this way one
        /// Injection gets one identifier however the name was typed (ADR 0007).
        /// </summary>
        public string Database
        {
            get => _database;
            init => _database = Upper(value);
        }

        /// <inheritdoc />
        public override string ToString() => $"{Database}/{Project}/{InjectionId}";

        // A snapshot without a database reads as null.
        private static string Upper(string? database) => database?.ToUpperInvariant() ?? "";
    }
}
