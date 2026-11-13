using System;
using System.Threading;
using System.Threading.Tasks;
using DocumentProcessor.WebForms.Data;
using DocumentProcessor.WebForms.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocumentProcessor.WebForms.Services
{
    /// <summary>Extracts text from a stored document and saves an AI-generated summary.</summary>
    public class DocumentPipeline
    {
        private readonly IDbContextFactory<DocumentDbContext> _dbFactory;
        private readonly IDocumentStorage _storage;
        private readonly DocumentTextExtractor _textExtractor;
        private readonly IDocumentSummarizer _summarizer;
        private readonly ILogger<DocumentPipeline> _logger;

        public DocumentPipeline(
            IDbContextFactory<DocumentDbContext> dbFactory,
            IDocumentStorage storage,
            DocumentTextExtractor textExtractor,
            IDocumentSummarizer summarizer,
            ILogger<DocumentPipeline> logger)
        {
            _dbFactory = dbFactory;
            _storage = storage;
            _textExtractor = textExtractor;
            _summarizer = summarizer;
            _logger = logger;
        }

        public async Task ProcessAsync(Guid documentId, CancellationToken cancellationToken = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            // The DbContext query filter already excludes soft-deleted rows; the explicit
            // condition is kept to match the original query.
            var document = await db.Documents
                .FirstOrDefaultAsync(d => d.Id == documentId && !d.IsDeleted, cancellationToken)
                .ConfigureAwait(false);

            if (document == null)
            {
                _logger.LogWarning("Document {DocumentId} not found; skipping processing.", documentId);
                return;
            }

            try
            {
                document.Status = DocumentStatus.Processing;
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                using (var content = _storage.OpenRead(document.StoragePath))
                {
                    var text = _textExtractor.Extract(document.FileExtension, content);

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        throw new InvalidOperationException(
                            "No text could be extracted from the document.");
                    }

                    document.Summary = await _summarizer
                        .SummarizeAsync(document.OriginalFileName, text, cancellationToken)
                        .ConfigureAwait(false);
                }

                document.Status = DocumentStatus.Processed;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Processing failed for document {DocumentId}.", documentId);
                document.Status = DocumentStatus.Failed;
            }

            // Persist the final status even if the caller's token was cancelled mid-summary.
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
