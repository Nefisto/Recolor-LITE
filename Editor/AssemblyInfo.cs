using System.Runtime.CompilerServices;

// The Pro features plug into the window through its internal parts.
[assembly: InternalsVisibleTo("Nefisto.Recolor.Pro.Editor")]

// The development tools, kept out of the packages, open bug reports through them too.
[assembly: InternalsVisibleTo("Nefisto.Recolor.Dev.Editor")]
