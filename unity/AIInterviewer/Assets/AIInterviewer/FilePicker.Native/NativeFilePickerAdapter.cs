using System;
using System.Collections.Generic;

namespace AIInterviewer.Platform
{
    /// <summary>
    /// Android/iOS document picker backed by yasirkula's Native File Picker (MIT,
    /// https://github.com/yasirkula/UnityNativeFilePicker). Compiled only when that package is installed.
    /// Picked files are copied by the plugin to a readable cache path that keeps the original file name.
    /// </summary>
    public sealed class NativeFilePickerAdapter : IFilePicker
    {
        public bool IsSupported => true;

        public bool CanPickMultiple => NativeFilePicker.CanPickMultipleFiles();

        public void Pick(string[] extensions, bool multiple, Action<string[]> onPicked)
        {
            var types = new List<string>();
            foreach (var ext in extensions)
            {
                string t = NativeFilePicker.ConvertExtensionToFileType(ext);
                if (!string.IsNullOrEmpty(t) && !types.Contains(t)) types.Add(t);
            }
            if (multiple && NativeFilePicker.CanPickMultipleFiles())
            {
                NativeFilePicker.PickMultipleFiles(paths => onPicked?.Invoke(paths), types.ToArray());
            }
            else
            {
                NativeFilePicker.PickFile(path => onPicked?.Invoke(path == null ? null : new[] { path }), types.ToArray());
            }
        }
    }
}
