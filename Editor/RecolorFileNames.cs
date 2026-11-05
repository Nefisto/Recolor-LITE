using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Nefisto.Recolor
{
    // The File name of Save as new image, for each image whose file name was changed, by GUID so it
    // survives a rename or a move. An image with none uses the default: its name with _recolored.
    // Kept as JSON in ProjectSettings, out of the project's Assets and shared through version control
    // with it; plain JSON, so Recolor and the copy inside Recolor Pro both read it.
    internal static class RecolorFileNames
    {
        internal const string Path = "ProjectSettings/RecolorFileNames.json";

        [Serializable]
        internal class Entry
        {
            public string imageGuid;
            public RecolorWindow.NameMode mode;
            public string nameStart;
            public string nameEnd;
            public string customName;
        }

        [Serializable]
        private class Data
        {
            public List<Entry> entries = new();
        }

        private static Data data;

        private static Data Loaded => data ??= Load();

        private static Data Load ()
        {
            try
            {
                if (File.Exists(Path))
                    return JsonUtility.FromJson<Data>(File.ReadAllText(Path)) ?? new Data();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Recolor] Couldn't read {Path}, so the file names set per image start over: {exception.Message}");
            }

            return new Data();
        }

        internal static Entry Find (string guid) => Loaded.entries.FirstOrDefault(each => each.imageGuid == guid);

        // Keeps the image's file name, or forgets it when it's back to the default.
        internal static void Set (string guid, RecolorWindow.NameMode mode, string nameStart, string nameEnd, string customName)
        {
            if (string.IsNullOrEmpty(guid))
                return;

            var isDefault = mode == RecolorWindow.NameMode.Suffix &&
                            string.IsNullOrEmpty(nameStart) &&
                            (string.IsNullOrEmpty(nameEnd) || nameEnd == RecolorWindow.Suffix) &&
                            string.IsNullOrEmpty(customName);

            var entry = Find(guid);

            if (isDefault)
            {
                if (entry == null)
                    return;

                Loaded.entries.Remove(entry);
            }
            else
            {
                if (entry == null)
                {
                    entry = new Entry { imageGuid = guid };
                    Loaded.entries.Add(entry);
                }

                entry.mode = mode;
                entry.nameStart = nameStart;
                entry.nameEnd = nameEnd;
                entry.customName = customName;
            }

            File.WriteAllText(Path, JsonUtility.ToJson(Loaded, true));
        }
    }
}
