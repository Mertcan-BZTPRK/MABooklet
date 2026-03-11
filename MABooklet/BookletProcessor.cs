using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System;
using System.Collections.Generic;

namespace MABooklet 
{
    public class BookletProcessor
    {
        public static void CreateImposedBooklet(string sourcePath, string destPath)
        {
            int originalPageCount;
            using (PdfDocument temp = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import))
            {
                originalPageCount = temp.PageCount;
            }

            int sheets = (int)Math.Ceiling(originalPageCount / 4.0);
            int totalPagesNeeded = sheets * 4;

            using (PdfDocument outputDoc = new PdfDocument())
            {
                List<int> pageOrder = CalculateBookletOrder(sheets, totalPagesNeeded);

                using (PdfDocument inputDoc = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import))
                {
                    foreach (int pageNum in pageOrder)
                    {
                        if (pageNum > originalPageCount) outputDoc.AddPage();
                        else outputDoc.AddPage(inputDoc.Pages[pageNum - 1]);
                    }
                }
                outputDoc.Save(destPath);
            }
        }

        public static void CreateImposedBookletWithScale(string sourcePath, string destPath, double scaleFactor)
        {
            int originalPageCount;
            using (PdfDocument temp = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import))
            {
                originalPageCount = temp.PageCount;
            }

            int sheets = (int)Math.Ceiling(originalPageCount / 4.0);
            int totalPagesNeeded = sheets * 4;

            using (PdfDocument outputDoc = new PdfDocument())
            {
                List<int> pageOrder = CalculateBookletOrder(sheets, totalPagesNeeded);

                for (int i = 0; i < pageOrder.Count; i += 2)
                {
                    PdfPage newPage = outputDoc.AddPage();
                    newPage.Width = XUnit.FromInch(17);
                    newPage.Height = XUnit.FromInch(11);
                    newPage.Orientation = PageOrientation.Landscape;

                    using (XGraphics gfx = XGraphics.FromPdfPage(newPage))
                    {
                        DrawScaledPage(gfx, sourcePath, pageOrder[i], originalPageCount, true, scaleFactor);

                        if (i + 1 < pageOrder.Count)
                            DrawScaledPage(gfx, sourcePath, pageOrder[i + 1], originalPageCount, false, scaleFactor);
                    }
                }
                outputDoc.Save(destPath);
            }
        }
        private static List<int> CalculateBookletOrder(int sheets, int total)
        {
            var list = new List<int>();
            int start = 1; int end = total;
            for (int i = 0; i < sheets; i++)
            {
                list.Add(end); list.Add(start);
                list.Add(start + 1); list.Add(end - 1);
                start += 2; end -= 2;
            }
            return list;
        }

        private static void DrawScaledPage(XGraphics gfx, string path, int pageNum, int maxPages, bool isLeft, double scaleFactor)
        {
            if (pageNum > maxPages) return;

         
            XPdfForm form = XPdfForm.FromFile(path);
            form.PageNumber = pageNum;

            double targetAreaW = XUnit.FromInch(8.5).Point;
            double targetAreaH = XUnit.FromInch(11).Point;

            double scaledW = form.PixelWidth * scaleFactor;
            double scaledH = form.PixelHeight * scaleFactor;
            double paperWidth = XUnit.FromInch(17).Point;
            double startX = isLeft ? 0 : paperWidth / 2.0;

            double x = startX + (targetAreaW - scaledW) / 2.0;
            double y = (targetAreaH - scaledH) / 2.0;

            gfx.DrawImage(form, x, y, scaledW, scaledH);
        }
    }
}