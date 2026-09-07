using CPCREDO.Application.Savings;
using CPCREDO.Domain.Common;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CPCREDO.Infrastructure.Savings;

internal static class StatementPdf
{
    static StatementPdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Render(SavingsStatementDto statement, string memberNo, string memberName)
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
                    col.Item().Text("Relevé de compte d’épargne").FontSize(14).Bold();
                    col.Item().PaddingTop(8).Text($"Membre : {memberNo} — {memberName}");
                    col.Item().Text($"Compte : {statement.AccountNo} ({statement.ProductName})");
                    col.Item().Text($"Période : {statement.From:yyyy-MM-dd} → {statement.To:yyyy-MM-dd}");
                    col.Item().Text($"Devise : {statement.CurrencyCode}");
                    col.Item().Text($"Solde comptable : {MoneyDisplay.Format(statement.LedgerBalance, statement.CurrencyCode)}");
                    col.Item().Text($"Solde disponible : {MoneyDisplay.Format(statement.AvailableBalance, statement.CurrencyCode)}");

                    col.Item().PaddingTop(12).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(80);
                            c.RelativeColumn(3);
                            c.ConstantColumn(50);
                            c.ConstantColumn(80);
                            c.ConstantColumn(80);
                        });

                        table.Header(h =>
                        {
                            h.Cell().Background(Colors.Green.Darken3).Padding(4).Text("Date").FontColor(Colors.White).Bold();
                            h.Cell().Background(Colors.Green.Darken3).Padding(4).Text("Libellé").FontColor(Colors.White).Bold();
                            h.Cell().Background(Colors.Green.Darken3).Padding(4).Text("Type").FontColor(Colors.White).Bold();
                            h.Cell().Background(Colors.Green.Darken3).Padding(4).Text("Montant").FontColor(Colors.White).Bold();
                            h.Cell().Background(Colors.Green.Darken3).Padding(4).Text("Solde").FontColor(Colors.White).Bold();
                        });

                        if (statement.Entries.Count == 0)
                        {
                            table.Cell().ColumnSpan(5).Padding(6).Text("Aucun mouvement sur la période.");
                        }
                        else
                        {
                            foreach (var entry in statement.Entries)
                            {
                                table.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .Text(entry.ValueDateUtc.ToString("yyyy-MM-dd"));
                                table.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .Text(entry.Description);
                                table.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .Text(entry.EntryType);
                                table.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .AlignRight().Text(MoneyDisplay.Format(entry.Amount, statement.CurrencyCode));
                                table.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .AlignRight().Text(MoneyDisplay.Format(entry.RunningBalance, statement.CurrencyCode));
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
            Title = $"Relevé {statement.AccountNo} — {Letterhead.Sigle}",
            Author = Letterhead.Sigle,
            Subject = $"{Letterhead.Line2} | {Letterhead.Line3} | {Letterhead.Line4}",
            Creator = Letterhead.Sigle
        })
        .GeneratePdf();
    }
}
