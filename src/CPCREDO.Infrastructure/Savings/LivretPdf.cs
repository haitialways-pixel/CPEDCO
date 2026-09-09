using CPCREDO.Application.Savings;
using CPCREDO.Domain.Common;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CPCREDO.Infrastructure.Savings;

internal static class LivretPdf
{
    static LivretPdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Render(LivretDto livret)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(32);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Grey.Darken4));

                page.Header().Column(col =>
                {
                    col.Item().Text(Letterhead.Sigle).FontSize(22).Bold().FontColor(Colors.Green.Darken3);
                    col.Item().Text(Letterhead.Line2).FontSize(12).SemiBold();
                    col.Item().Text(Letterhead.Line3).FontSize(10).Italic();
                    col.Item().Text(Letterhead.Line4).FontSize(8).FontColor(Colors.Grey.Darken2);
                    col.Item().PaddingTop(8).LineHorizontal(0.5f).LineColor(Colors.Green.Darken3);
                });

                page.Content().PaddingTop(14).Column(col =>
                {
                    col.Item().Text("Livret d’épargne").FontSize(14).Bold();
                    col.Item().PaddingTop(6).Text($"Membre : {livret.MemberNo} — {livret.MemberName}");
                    col.Item().Text($"N° compte : {livret.AccountNo}");
                    col.Item().Text($"Produit : {livret.ProductName}");
                    col.Item().Text($"Devise : {livret.CurrencyCode}");
                    col.Item().Text($"Période : {livret.From:yyyy-MM-dd} → {livret.To:yyyy-MM-dd}");

                    col.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(72);
                            c.RelativeColumn(3);
                            c.ConstantColumn(70);
                            c.ConstantColumn(70);
                            c.ConstantColumn(78);
                            c.ConstantColumn(78);
                        });

                        table.Header(h =>
                        {
                            h.Cell().Background(Colors.Green.Darken3).Padding(4).Text("Date").FontColor(Colors.White).Bold();
                            h.Cell().Background(Colors.Green.Darken3).Padding(4).Text("Libellé").FontColor(Colors.White).Bold();
                            h.Cell().Background(Colors.Green.Darken3).Padding(4).Text("Débit").FontColor(Colors.White).Bold();
                            h.Cell().Background(Colors.Green.Darken3).Padding(4).Text("Crédit").FontColor(Colors.White).Bold();
                            h.Cell().Background(Colors.Green.Darken3).Padding(4).Text("Solde").FontColor(Colors.White).Bold();
                            h.Cell().Background(Colors.Green.Darken3).Padding(4).Text("Caissier").FontColor(Colors.White).Bold();
                        });

                        if (livret.Lines.Count == 0)
                        {
                            table.Cell().ColumnSpan(6).Padding(6).Text("Aucun mouvement sur la période.");
                        }
                        else
                        {
                            foreach (var line in livret.Lines)
                            {
                                table.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .Text(line.ValueDateUtc.ToString("yyyy-MM-dd"));
                                table.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .Text(line.Description);
                                table.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .AlignRight().Text(line.Debit == 0m ? "" : MoneyDisplay.Format(line.Debit, livret.CurrencyCode));
                                table.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .AlignRight().Text(line.Credit == 0m ? "" : MoneyDisplay.Format(line.Credit, livret.CurrencyCode));
                                table.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .AlignRight().Text(MoneyDisplay.Format(line.RunningBalance, livret.CurrencyCode));
                                table.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .Text(line.CashierName);
                            }
                        }
                    });

                    col.Item().PaddingTop(14).Text(last =>
                    {
                        if (livret.LastOperationAtUtc is { } at && livret.LastOperationAmount is { } amount)
                        {
                            last.Span("Dernière opération : ").SemiBold();
                            last.Span($"{at:yyyy-MM-dd} · {MoneyDisplay.Format(amount, livret.CurrencyCode)}");
                            if (!string.IsNullOrWhiteSpace(livret.LastOperationType))
                                last.Span($" ({livret.LastOperationType})");
                        }
                        else
                        {
                            last.Span("Dernière opération : aucune");
                        }
                    });
                    col.Item().Text(t =>
                    {
                        t.Span("Solde disponible : ").SemiBold();
                        t.Span(MoneyDisplay.Format(livret.AvailableBalance, livret.CurrencyCode));
                    });
                    col.Item().PaddingTop(8).Text("Document non négociable").Bold().FontSize(10);
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span($"{Letterhead.Footer} · ").FontSize(7);
                    text.Span("page ");
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        })
        .WithMetadata(new DocumentMetadata
        {
            Title = $"Livret {livret.AccountNo} — {Letterhead.Sigle}",
            Author = Letterhead.Sigle,
            Subject = $"{Letterhead.Line2} | {Letterhead.Line3} | {Letterhead.Line4}",
            Creator = Letterhead.Sigle
        })
        .GeneratePdf();
    }
}
