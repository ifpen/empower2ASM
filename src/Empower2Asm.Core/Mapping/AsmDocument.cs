using System;
using System.Collections.Generic;
using Empower2Asm.Core.Source;
using IFPEN.AllotropeConverters.AllotropeModels.Hplc.Model;

namespace Empower2Asm.Core.Mapping
{
    /// <summary>
    /// One Injection's ASM Document, ready to be written with <see cref="AsmJson"/>. PDA spectra are too large to be held
    /// in the model (some 16 million values for a 30-minute run): the model holds a placeholder for each, and the spectra
    /// are written in its place, straight from Empower's values (ADR 0005).
    /// </summary>
    public sealed class AsmDocument
    {
        internal AsmDocument(LiquidChromatographySimpleModel model, IReadOnlyList<StreamedSpectra> spectra)
        {
            Model = model;
            Spectra = spectra;
        }

        /// <summary>
        /// The document's model, a placeholder standing for the absorbance values of each PDA Channel's spectra: only
        /// valid once written by <see cref="AsmJson"/>, which is why it isn't public.
        /// </summary>
        internal LiquidChromatographySimpleModel Model { get; }

        internal IReadOnlyList<StreamedSpectra> Spectra { get; }
    }

    /// <summary>
    /// The spectra of one PDA Channel, written where <see cref="Placeholder"/> stands in the model: every value times
    /// <see cref="Factor"/>, spectrum after spectrum.
    /// </summary>
    internal sealed class StreamedSpectra
    {
        public StreamedSpectra(SpectrumCube cube, decimal factor)
        {
            Cube = cube;
            Factor = factor;
        }

        /// <summary>The string the model holds instead of the values, unique to these spectra.</summary>
        public string Placeholder { get; } = "empower2asm-spectra-" + Guid.NewGuid().ToString("N");

        public SpectrumCube Cube { get; }

        public decimal Factor { get; }
    }
}
