using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace AIInterviewer.Platform
{
    /// <summary>Native document picker abstraction.</summary>
    public interface IFilePicker
    {
        bool IsSupported { get; }

        /// <summary>True if several files can be chosen in one go.</summary>
        bool CanPickMultiple { get; }

        /// <summary>Opens the picker. The callback receives readable file paths, or null if cancelled.</summary>
        void Pick(string[] extensions, bool multiple, Action<string[]> onPicked);
    }

    public sealed class PickedDocument
    {
        public string FileName;
        public byte[] Data;
    }

    public static class FilePicker
    {
        public static readonly string[] DocumentExtensions = { "pdf", "docx", "txt" };
        public const long MaxBytes = 2 * 1024 * 1024;

        private const string NativeAdapterType = "AIInterviewer.Platform.NativeFilePickerAdapter, AIInterviewer.FilePicker.Native";

        public static IFilePicker Create()
        {
#if UNITY_EDITOR
            return new EditorFilePicker();
#else
            var type = Type.GetType(NativeAdapterType);
            if (type != null) return (IFilePicker)Activator.CreateInstance(type);
            Debug.LogWarning("[FilePicker] Native File Picker package not installed; file upload unavailable.");
            return new UnsupportedFilePicker();
#endif
        }

        /// <summary>Picks documents and reads them. Throws <see cref="IOException"/> with a user-facing message.</summary>
        public static Task<List<PickedDocument>> PickDocumentsAsync(IFilePicker picker, bool multiple)
        {
            var tcs = new TaskCompletionSource<List<PickedDocument>>();
            picker.Pick(DocumentExtensions, multiple, paths =>
            {
                try
                {
                    var result = new List<PickedDocument>();
                    if (paths != null)
                    {
                        foreach (var path in paths)
                        {
                            if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                            var info = new FileInfo(path);
                            if (info.Length > MaxBytes)
                            {
                                throw new IOException($"{info.Name} is larger than 2 MB.");
                            }
                            byte[] data = File.ReadAllBytes(path);
                            result.Add(new PickedDocument { FileName = NormalizeName(info.Name, data), Data = data });
                        }
                    }
                    tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            return tcs.Task;
        }

        /// <summary>Ensures a supported extension (some providers hand over files without one).</summary>
        public static string NormalizeName(string name, byte[] data)
        {
            string ext = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
            if (Array.IndexOf(DocumentExtensions, ext) >= 0) return name;
            string sniffed = data.Length >= 4 && data[0] == '%' && data[1] == 'P' && data[2] == 'D' && data[3] == 'F' ? "pdf"
                : data.Length >= 2 && data[0] == 'P' && data[1] == 'K' ? "docx" : "txt";
            return Path.GetFileNameWithoutExtension(name) + "." + sniffed;
        }
    }

#if UNITY_EDITOR
    /// <summary>Editor: the OS file dialog (one file per pick).</summary>
    internal sealed class EditorFilePicker : IFilePicker
    {
        public bool IsSupported => true;
        public bool CanPickMultiple => false;

        public void Pick(string[] extensions, bool multiple, Action<string[]> onPicked)
        {
            string path = UnityEditor.EditorUtility.OpenFilePanelWithFilters("Choose a document", "",
                new[] { "Documents", string.Join(",", extensions) });
            onPicked?.Invoke(string.IsNullOrEmpty(path) ? null : new[] { path });
        }
    }
#endif

    internal sealed class UnsupportedFilePicker : IFilePicker
    {
        public bool IsSupported => false;
        public bool CanPickMultiple => false;

        public void Pick(string[] extensions, bool multiple, Action<string[]> onPicked) => onPicked?.Invoke(null);
    }
}
