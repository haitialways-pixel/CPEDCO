using CPCREDO.Application.Common;
using CPCREDO.Domain.Members;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace CPCREDO.Infrastructure.Members;

public static class KycImageProcessor
{
    public const int PhotoEdge = 600;
    public const int PhotoMaxBytes = 240 * 1024;
    public const double IdAspect = 85.6 / 54.0;
    public const double SignatureAspect = 3.0;
    public const int IdWidth = 856;
    public const int SignatureWidth = 900;

    public readonly record struct Processed(byte[] Bytes, string Extension, string ContentType);

    public static Result<Processed> Process(KycDocumentType type, byte[] input, string sourceExtension)
    {
        if (sourceExtension is "pdf")
            return Result<Processed>.Fail("kyc.content_type", "JPG ou PNG uniquement pour ces pièces.");

        try
        {
            using var image = Image.Load<Rgba32>(input);
            return type switch
            {
                KycDocumentType.Photo => EncodePhoto(image),
                KycDocumentType.IdFront or KycDocumentType.IdBack =>
                    EncodeAspect(image, IdAspect, IdWidth, sourceExtension, pad: true),
                KycDocumentType.Signature =>
                    EncodeAspect(image, SignatureAspect, SignatureWidth, sourceExtension, pad: false),
                _ => Result<Processed>.Fail("kyc.type", "Type de pièce inconnu.")
            };
        }
        catch (UnknownImageFormatException)
        {
            return Result<Processed>.Fail("kyc.content_type", "Le contenu du fichier ne correspond pas au format annoncé.");
        }
        catch (InvalidImageContentException)
        {
            return Result<Processed>.Fail("kyc.content_type", "Le contenu du fichier ne correspond pas au format annoncé.");
        }
    }

    private static Result<Processed> EncodePhoto(Image<Rgba32> image)
    {
        image.Mutate(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(PhotoEdge, PhotoEdge),
            Mode = ResizeMode.Crop,
            Position = AnchorPositionMode.Center
        }));

        foreach (var quality in new[] { 85, 75, 65, 55, 45, 35, 25, 15, 10 })
        {
            using var output = new MemoryStream();
            image.Save(output, new JpegEncoder { Quality = quality });
            if (output.Length <= PhotoMaxBytes)
                return Result<Processed>.Ok(new Processed(output.ToArray(), "jpg", "image/jpeg"));
        }

        return Result<Processed>.Fail(
            "kyc.photo_too_heavy",
            "La photo dépasse 240 Ko après compression. Recadrez ou choisissez une autre image.");
    }

    private static Result<Processed> EncodeAspect(
        Image<Rgba32> image,
        double aspect,
        int targetWidth,
        string sourceExtension,
        bool pad)
    {
        var targetHeight = Math.Max(1, (int)Math.Round(targetWidth / aspect));
        image.Mutate(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(targetWidth, targetHeight),
            Mode = pad ? ResizeMode.Pad : ResizeMode.Crop,
            Position = AnchorPositionMode.Center,
            PadColor = Color.White
        }));

        var png = sourceExtension == "png";
        using var output = new MemoryStream();
        if (png)
            image.Save(output, new PngEncoder());
        else
            image.Save(output, new JpegEncoder { Quality = 85 });

        if (output.Length > KycDocumentService.MaxBytes)
            return Result<Processed>.Fail("kyc.too_large", "Fichier trop volumineux (max. 5 Mo).");

        return Result<Processed>.Ok(new Processed(
            output.ToArray(),
            png ? "png" : "jpg",
            png ? "image/png" : "image/jpeg"));
    }
}
