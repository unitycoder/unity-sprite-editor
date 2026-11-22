using System;
using System.IO;
using System.Text.RegularExpressions;
using BetterSpriteEditor.Model;
using BetterSpriteEditor.Operations;
using UnityEngine;

namespace BetterSpriteEditor.DataAccess
{
    [Serializable]
    internal sealed class BatchSliceBackup
    {
        public int Version = 1;
        public DocumentSnapshotDto Before;
        public string AfterRevision;
        public string[] AfterRegionOrder;
        public string Status = "Pending";
    }

    internal sealed class BatchSliceBackups
    {
        internal const string DefaultRoot = "UserSettings/BetterSpriteEditor/batch-slicing";
        private readonly string root;
        internal string RunDirectory { get; private set; }

        internal BatchSliceBackups(string root = DefaultRoot) { this.root = root; }

        internal void Begin()
        {
            string run = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N");
            RunDirectory = Path.Combine(root, run);
            Directory.CreateDirectory(RunDirectory);
            AssetState.WriteAtomic(Path.Combine(root, "last-run.txt"), run);
        }

        internal string[] LastFiles()
        {
            string pointer = Path.Combine(root, "last-run.txt");
            if (!File.Exists(pointer)) return Array.Empty<string>();
            string run = File.ReadAllText(pointer);
            if (!Regex.IsMatch(run, @"\A\d{8}-\d{6}-[0-9a-f]{32}\z")) throw new InvalidDataException("Invalid batch backup index.");
            string directory = Path.Combine(root, run);
            return Directory.Exists(directory) ? Directory.GetFiles(directory, "*.json") : Array.Empty<string>();
        }

        internal string WriteBefore(SpriteDocument document)
        {
            if (RunDirectory == null) throw new InvalidOperationException("Start a batch backup before saving textures.");
            var record = new BatchSliceBackup
            {
                Before = DocumentSnapshot.Capture(document, new HistoryStack(document), new SelectionModel())
            };
            string path = Path.Combine(RunDirectory, document.AssetGuid + ".json");
            if (File.Exists(path)) throw new InvalidOperationException("This texture already has a backup in this run. Preview a new batch before retrying it.");
            Write(path, record);
            return path;
        }

        internal static BatchSliceBackup Read(string path)
        {
            var record = JsonUtility.FromJson<BatchSliceBackup>(File.ReadAllText(path));
            if (record == null || record.Version != 1) throw new InvalidDataException("Unsupported batch backup format.");
            DocumentSnapshot.Validate(record.Before);
            return record;
        }

        internal static void Write(string path, BatchSliceBackup record) => AssetState.WriteAtomic(path, JsonUtility.ToJson(record));
    }
}
