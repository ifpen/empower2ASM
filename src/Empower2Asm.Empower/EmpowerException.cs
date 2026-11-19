using System;

namespace Empower2Asm.Empower
{
    /// <summary>An Empower read failed, with a message meant for the person running the tool.</summary>
    public sealed class EmpowerException : Exception
    {
        /// <summary>Creates the exception.</summary>
        public EmpowerException(string message, Exception? inner = null)
            : base(message, inner)
        {
        }
    }
}
