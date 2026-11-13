using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Components.Forms;

namespace DocumentProcessor.WebForms.Components.Controls.Controls
{
    /// <summary>
    /// Raised by <see cref="DocumentUploader"/> once the selected files have passed the file
    /// count, size, extension and <c>DocumentTextExtractor.CanExtract</c> checks. Replaces the
    /// Web Forms version that carried <c>HttpPostedFile</c> instances.
    /// </summary>
    public class UploadEventArgs : EventArgs
    {
        public UploadEventArgs(IList<IBrowserFile> files, long maxAllowedSize)
        {
            Files = files;
            MaxAllowedSize = maxAllowedSize;
        }

        public IList<IBrowserFile> Files { get; private set; }

        /// <summary>The per-file byte limit (Storage.MaxFileSizeMegabytes) the files were checked against.</summary>
        public long MaxAllowedSize { get; private set; }

        /// <summary>Opens the browser file for reading, allowing up to the configured per-file size.</summary>
        public Stream OpenReadStream(IBrowserFile file)
        {
            return file.OpenReadStream(MaxAllowedSize);
        }
    }
}
