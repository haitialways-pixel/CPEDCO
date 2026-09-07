using CPCREDO.Domain.Common;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CPCREDO.Infrastructure.Reports;

internal static class ReportPdf
{
    static ReportPdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Render(
        string title,
        string subtitle,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows,
        string? note = null)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Grey.Darken4));

                page.Header().Column(col =>
                {
                    col.Item().Text(Letterhead.Sigle).FontSize(22).Bold().FontColor(Colors.Green.Darken3);
                    col.Item().Text(Letterhead.Line2).FontSize(12).SemiBold();
                    col.Item().Text(Letterhead.Line3).FontSize(10).Italic();
                    col.Item().Text(Letterhead.Line4).FontSize(8).FontColor(Colors.Grey.Darken2);
                    col.Item().PaddingTop(8).LineHorizontal(0.5f).LineColor(Colors.Green.Darken3);
                });

                page.Content().PaddingTop(16).Column(col =>
                {
                    col.Item().Text(title).FontSize(14).Bold();
                    col.Item().PaddingTop(4).Text(subtitle);
                    if (!string.IsNullOrWhiteSpace(note))
                        col.Item().PaddingTop(4).Text(note).Italic();

                    col.Item().PaddingTop(12).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            foreach (var _ in headers)
                                c.RelativeColumn();
                        });

                        table.Header(h =>
                        {
                            foreach (var header in headers)
                            {
                                h.Cell().Background(Colors.Green.Darken3).Padding(4)
                                    .Text(header).FontColor(Colors.White).Bold();
                            }
                        });

                        if (rows.Count == 0)
                        {
                            table.Cell().ColumnSpan((uint)headers.Count).Padding(6)
                                .Text("Aucune ligne pour cette période.");
                        }
                        else
                        {
                            foreach (var row in rows)
                            {
                                foreach (var cell in row)
                                {
                                    table.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                        .Text(cell);
                                }
                            }
                        }
                    });
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span($"{Letterhead.Sigle} · {Letterhead.Line4} · ").FontSize(8);
                    text.Span("page ");
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        })
        .WithMetadata(new DocumentMetadata
        {
            Title = $"{title} — {Letterhead.Sigle}",
            Author = Letterhead.Sigle,
            Subject = $"{Letterhead.Line2} | {Letterhead.Line3} | {Letterhead.Line4}",
            Creator = Letterhead.Sigle
        })
        .GeneratePdf();
    }
}
