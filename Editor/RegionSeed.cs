using System;
using UnityEngine;

namespace Nefisto.Recolor
{
    // A click on the original image that selects a part, with the way the part was grown from it. The way
    // stays with the click, so changing the options later only changes the clicks made after.
    [Serializable]
    public struct RegionSeed
    {
        public Vector2Int point;
        public bool contiguous;
        public bool diagonals;

        // Takes what it reaches out of the selection instead of adding it.
        public bool remove;

        // Takes every pixel the From colour matches, as the whole image would change, wherever it is. It
        // follows the matching settings, so changing them changes what it takes.
        public bool everyMatch;

        // Every pixel the From colour matches.
        public static RegionSeed EveryMatch () => new() { everyMatch = true, contiguous = false };

        public RegionSeed (Vector2Int point, bool contiguous, bool diagonals, bool remove = false)
        {
            this.point = point;
            this.contiguous = contiguous;
            this.diagonals = diagonals;
            this.remove = remove;
            everyMatch = false;
        }
    }
}
