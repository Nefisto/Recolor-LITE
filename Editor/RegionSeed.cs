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

        public RegionSeed (Vector2Int point, bool contiguous, bool diagonals, bool remove = false)
        {
            this.point = point;
            this.contiguous = contiguous;
            this.diagonals = diagonals;
            this.remove = remove;
        }
    }
}
