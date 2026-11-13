using System;

namespace DocumentProcessor.WebForms.Components.Controls.Controls
{
    /// <summary>
    /// Payload of the DocumentList component's ViewSummaryRequested and DeleteRequested callbacks
    /// (moved from the legacy Controls/DocumentList.ascx.cs).
    /// </summary>
    public class DocumentEventArgs : EventArgs
    {
        public DocumentEventArgs(Guid documentId)
        {
            DocumentId = documentId;
        }

        public Guid DocumentId { get; private set; }
    }
}
