namespace Empower2Asm.Core.Source
{
    /// <summary>
    /// Identifies one Injection across Empower databases and Projects. Empower Injection IDs are only unique within
    /// one Project of one database, so all three parts are needed.
    /// </summary>
    /// <param name="Database">The Empower database name.</param>
    /// <param name="Project">The Empower Project name.</param>
    /// <param name="InjectionId">The Empower Injection ID, unique within the Project.</param>
    public sealed record InjectionKey(string Database, string Project, int InjectionId)
    {
        /// <inheritdoc />
        public override string ToString() => $"{Database}/{Project}/{InjectionId}";
    }
}
