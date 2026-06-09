using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ClinicalHealthcare.Infrastructure.Pdf;

/// <summary>
/// Generates an in-memory appointment confirmation PDF using the QuestPDF fluent API.
///
/// Contract:
///   - No file I/O — output is returned as a <c>byte[]</c>.
///   - Target size &lt; 200KB; typical generation &lt; 500ms (AC-002).
///   - Community-licence declaration is set once at startup — callers do not need to configure it.
/// </summary>
public static class ConfirmationPdfGenerator
{
    static ConfirmationPdfGenerator()
    {
        // QuestPDF 2023+ requires an explicit licence setting.
        // Community licence is free for personal, open-source, and small-business use.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>
    /// Generates a PDF appointment confirmation for the given data.
    /// </summary>
    /// <param name="dto">Appointment details used to populate the PDF.</param>
    /// <returns>Raw PDF bytes — ready to attach to an email.</returns>
    public static byte[] Generate(AppointmentConfirmationDto dto)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(t => t.FontSize(11).FontFamily(Fonts.Arial));

                page.Header().Element(ComposeHeader);

                page.Content().Element(c => ComposeBody(c, dto));

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("ClinicalHub · Appointment Confirmation · Generated ")
                        .FontSize(9).FontColor(Colors.Grey.Medium);
                    text.Span(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC")
                        .FontSize(9).FontColor(Colors.Grey.Medium);
                });
            });
        });

        return document.GeneratePdf();
    }

    // ── Header ────────────────────────────────────────────────────────────────

    private static void ComposeHeader(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(inner =>
                {
                    inner.Item().Text("ClinicalHub").FontSize(20).Bold().FontColor(Colors.Blue.Darken3);
                    inner.Item().Text("Appointment Confirmation").FontSize(13).FontColor(Colors.Grey.Darken1);
                });
                row.ConstantItem(100).Height(40).Background(Colors.Blue.Darken3)
                   .AlignCenter().AlignMiddle()
                   .Text("CONFIRMED").FontSize(10).Bold().FontColor(Colors.White);
            });

            col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Blue.Darken3);
        });
    }

    // ── Body ─────────────────────────────────────────────────────────────────

    private static void ComposeBody(IContainer container, AppointmentConfirmationDto dto)
    {
        container.PaddingTop(24).Column(col =>
        {
            // Patient section
            col.Item().Text("Patient Information").Bold().FontSize(13);
            col.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(160);
                    c.RelativeColumn();
                });

                AddRow(table, "Name",           dto.PatientFullName);
                AddRow(table, "Email",          dto.PatientEmail);
                AddRow(table, "Reference No.",  $"APT-{dto.AppointmentId:D6}");
            });

            col.Item().PaddingTop(20).Text("Appointment Details").Bold().FontSize(13);
            col.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(160);
                    c.RelativeColumn();
                });

                AddRow(table, "Date",     dto.SlotTime.ToString("dddd, dd MMMM yyyy"));
                AddRow(table, "Time",     dto.SlotTime.ToString("HH:mm") + " UTC");
                AddRow(table, "Duration", $"{dto.DurationMinutes} minutes");
                AddRow(table, "Status",   dto.Status);
            });

            col.Item().PaddingTop(28).Background(Colors.Blue.Lighten5).Padding(12).Text(text =>
            {
                text.Span("Please arrive 10 minutes before your appointment. ")
                    .FontSize(10).FontColor(Colors.Blue.Darken3);
                text.Span("Bring a valid photo ID.")
                    .FontSize(10).FontColor(Colors.Blue.Darken3);
            });
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void AddRow(TableDescriptor table, string label, string value)
    {
        table.Cell().Background(Colors.Grey.Lighten4).PaddingHorizontal(8).PaddingVertical(5)
             .Text(label).Bold().FontSize(10);
        table.Cell().PaddingHorizontal(8).PaddingVertical(5)
             .Text(value).FontSize(10);
    }
}

/// <summary>Data transferred to <see cref="ConfirmationPdfGenerator.Generate"/>.</summary>
public sealed record AppointmentConfirmationDto(
    int      AppointmentId,
    string   PatientFullName,
    string   PatientEmail,
    DateTime SlotTime,
    int      DurationMinutes,
    string   Status);
