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
                page.Size(PageSizes.A6);
                page.Margin(16);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Black));

                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                    });

                    table.Header(h =>
                    {
                        h.Cell().Padding(3).Text("Date").Bold();
                        h.Cell().Padding(3).AlignRight().Text("Débit").Bold();
                        h.Cell().Padding(3).AlignRight().Text("Crédit").Bold();
                        h.Cell().Padding(3).AlignRight().Text("Solde").Bold();
                    });

                    if (livret.Lines.Count == 0)
                    {
                        table.Cell().ColumnSpan(4).Padding(6).Text("Rien de nouveau à porter sur le livret.");
                    }
                    else
                    {
                        foreach (var line in livret.Lines)
                        {
                            table.Cell().Padding(3).Text(line.ValueDateUtc.ToString("yyyy-MM-dd"));
                            table.Cell().Padding(3).AlignRight().Text(line.Debit == 0m ? "" : MoneyDisplay.Format(line.Debit, livret.CurrencyCode));
                            table.Cell().Padding(3).AlignRight().Text(line.Credit == 0m ? "" : MoneyDisplay.Format(line.Credit, livret.CurrencyCode));
                            table.Cell().Padding(3).AlignRight().Text(MoneyDisplay.Format(line.RunningBalance, livret.CurrencyCode));
                        }
                    }
                });
            });
        }).GeneratePdf();
    }
}
