using System;
using OpenCvSharp;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.Services;

public static class ImgArithmeticProcessor
{
    public static Mat Run(Mat matA, Mat matB, ImgArithmeticDefinition def)
    {
        if (matA is null || matA.IsDisposed || matA.Empty())
        {
            return matB is not null && !matB.IsDisposed && !matB.Empty() ? matB.Clone() : new Mat();
        }

        if (def is null || def.Op == ImgArithmeticOp.BIT_NOT)
        {
            var notRes = new Mat();
            Cv2.BitwiseNot(matA, notRes);
            return notRes;
        }

        if (matB is null || matB.IsDisposed || matB.Empty())
        {
            return matA.Clone();
        }

        // Match dimensions & channels between matA and matB
        using var preparedB = AlignMat(matB, matA.Size(), matA.Type());
        var res = new Mat();

        switch (def.Op)
        {
            case ImgArithmeticOp.ADD:
                Cv2.AddWeighted(matA, def.WeightA, preparedB, def.WeightB, def.Offset, res);
                break;
            case ImgArithmeticOp.SUB:
                Cv2.Absdiff(matA, preparedB, res);
                break;
            case ImgArithmeticOp.MIN:
                Cv2.Min(matA, preparedB, res);
                break;
            case ImgArithmeticOp.MAX:
                Cv2.Max(matA, preparedB, res);
                break;
            case ImgArithmeticOp.BIT_AND:
                Cv2.BitwiseAnd(matA, preparedB, res);
                break;
            case ImgArithmeticOp.BIT_OR:
                Cv2.BitwiseOr(matA, preparedB, res);
                break;
            case ImgArithmeticOp.BIT_XOR:
                Cv2.BitwiseXor(matA, preparedB, res);
                break;
            case ImgArithmeticOp.BIT_NOT:
                Cv2.BitwiseNot(matA, res);
                break;
            default:
                Cv2.Absdiff(matA, preparedB, res);
                break;
        }

        return res;
    }

    private static Mat AlignMat(Mat src, Size targetSize, MatType targetType)
    {
        var resized = new Mat();
        if (src.Size() != targetSize)
        {
            Cv2.Resize(src, resized, targetSize);
        }
        else
        {
            src.CopyTo(resized);
        }

        if (resized.Type() != targetType)
        {
            var converted = new Mat();
            if (targetType == MatType.CV_8UC1 && resized.Channels() == 3)
            {
                Cv2.CvtColor(resized, converted, ColorConversionCodes.BGR2GRAY);
            }
            else if (targetType == MatType.CV_8UC3 && resized.Channels() == 1)
            {
                Cv2.CvtColor(resized, converted, ColorConversionCodes.GRAY2BGR);
            }
            else
            {
                resized.ConvertTo(converted, targetType);
            }
            resized.Dispose();
            return converted;
        }

        return resized;
    }
}
