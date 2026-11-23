# PDA spectra are written spectrum after spectrum, in full

A PDA Channel's spectra go in the `three-dimensional ultraviolet spectrum data cube` as one flat array of absorbance values over two explicit axes, retention time then wavelength. ASM doesn't say in what order a flat array covers two axes, and nothing in the document can show it. We write one spectrum after another, wavelength varying fastest: value `t × (number of wavelengths) + w` is the absorbance at time `t` and wavelength `w`. That is the order of Allotrope's only real-instrument example of this cube (its automated reactors example) and the order Empower returns spectra in, so the cube is written as it is read, one spectrum at a time, never held as a whole in the document's model. Every value is written as 2D traces' are, to the 7 significant digits a float holds (some floats need 9 to be told apart, which would only add noise), and none is left out, whatever the size: a 30-minute run at 40 spectra per second over 221 wavelengths is about 16 million values, some 150 MB of JSON.

## Considered Options

- Time varying fastest: rejected, as it matches neither Allotrope's example nor how Empower reads, and would need the whole cube in memory to transpose.
- The wavelength axis as an ASM function (start, increment, length) when evenly spaced: rejected, as the list costs nothing next to the values and consumers (allotropy included) only expect lists.
- Fewer digits, or thinning out times or wavelengths: rejected, as the cube is there so the raw data is available.

## Consequences

- A consumer reading the cube the other way round gets every spectrum transposed without any error. Changing the order later would silently break every document already written.
- Writing an ASM Document streams to its file rather than building it in memory first.
