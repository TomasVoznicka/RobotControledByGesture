﻿//----------------------------------------------------------------------------
//  Copyright (C) 2004-2016 by EMGU Corporation. All rights reserved.       
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.IO;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.OCR;
using Emgu.CV.Structure;
using Emgu.CV.Text;
using Emgu.CV.Util;

namespace OCR
{
   public partial class OCRForm : Form
   {
      private Tesseract _ocr;

      public OCRForm()
      {
         InitializeComponent();
         InitOcr("", "eng", OcrEngineMode.TesseractCubeCombined);
         ocrOptionsComboBox.SelectedIndex = 0;

         Mat img = new Mat(200, 400, DepthType.Cv8U, 3); //Create a 3 channel image of 400x200
         img.SetTo(new Bgr(255, 0, 0).MCvScalar); // set it to Blue color

         //Draw "Hello, world." on the image using the specific font
         CvInvoke.PutText(
            img,
            "Hello, world",
            new System.Drawing.Point(10, 80),
            FontFace.HersheyComplex,
            1.0,
            new Bgr(0, 255, 0).MCvScalar);
         OcrImage(img);
      }

      private void InitOcr(String path, String lang, OcrEngineMode mode)
      {
         try
         {
            if (_ocr != null)
            {
               _ocr.Dispose();
               _ocr = null;
            }
            _ocr = new Tesseract(path, lang, mode);
            languageNameLabel.Text = String.Format("{0} : {1}", lang, mode.ToString());
         }
         catch (Exception e)
         {
            _ocr = null;
            MessageBox.Show(e.Message, "Failed to initialize tesseract OCR engine", MessageBoxButtons.OK);
            languageNameLabel.Text = "Failed to initialize tesseract OCR engine";
         }
      }

      /// <summary>
      /// The OCR mode
      /// </summary>
      private enum OCRMode
      {
         /// <summary>
         /// Perform a full page OCR
         /// </summary>
         FullPage,

         /// <summary>
         /// Detect the text region before applying OCR.
         /// </summary>
         TextDetection
      }

      private OCRMode Mode
      {
         get { return ocrOptionsComboBox.SelectedIndex == 0 ? OCRMode.FullPage : OCRMode.TextDetection; }
      }

      private static Rectangle ScaleRectangle(Rectangle r, double scale)
      {
         double centerX = r.Location.X + r.Width / 2.0;
         double centerY = r.Location.Y + r.Height / 2.0;
         double newWidth = Math.Round(r.Width * scale);
         double newHeight = Math.Round(r.Height * scale);
         return new Rectangle((int)Math.Round(centerX - newWidth / 2.0), (int)Math.Round(centerY - newHeight / 2.0),
            (int)newWidth, (int)newHeight);
      }

      private static String OcrImage(Tesseract ocr, Mat image, OCRMode mode, Mat imageColor)
      {
         Bgr drawCharColor = new Bgr(Color.Blue);
         
         if (image.NumberOfChannels == 1)
            CvInvoke.CvtColor(image, imageColor, ColorConversion.Gray2Bgr);
         else
            image.CopyTo(imageColor);

         if (mode == OCRMode.FullPage)
         {
            ocr.Recognize(image);
            Tesseract.Character[] characters = ocr.GetCharacters();
            if (characters.Length == 0)
            {
               Mat imgGrey = new Mat();
               CvInvoke.CvtColor(image, imgGrey, ColorConversion.Bgr2Gray);
               Mat imgThresholded = new Mat();
               CvInvoke.Threshold(imgGrey, imgThresholded, 65, 255, ThresholdType.Binary);
               ocr.Recognize(imgThresholded);
               characters = ocr.GetCharacters();
               imageColor = imgThresholded;
               if (characters.Length == 0)
               {
                  CvInvoke.Threshold(image, imgThresholded, 190, 255, ThresholdType.Binary);
                  ocr.Recognize(imgThresholded);
                  characters = ocr.GetCharacters();
                  imageColor = imgThresholded;
               }
            }
            foreach (Tesseract.Character c in characters)
            {
               CvInvoke.Rectangle(imageColor, c.Region, drawCharColor.MCvScalar);
            }

            return ocr.GetText();
         }
         else
         {
            bool checkInvert = true;

            Rectangle[] regions;

            using (
               ERFilterNM1 er1 = new ERFilterNM1("trained_classifierNM1.xml", 8, 0.00025f, 0.13f, 0.4f, true, 0.1f))
            using (ERFilterNM2 er2 = new ERFilterNM2("trained_classifierNM2.xml", 0.3f))
            {
               int channelCount = image.NumberOfChannels;
               UMat[] channels = new UMat[checkInvert ? channelCount * 2 : channelCount];

               for (int i = 0; i < channelCount; i++)
               {
                  UMat c = new UMat();
                  CvInvoke.ExtractChannel(image, c, i);
                  channels[i] = c;
               }

               if (checkInvert)
               {
                  for (int i = 0; i < channelCount; i++)
                  {
                     UMat c = new UMat();
                     CvInvoke.BitwiseNot(channels[i], c);
                     channels[i + channelCount] = c;
                  }
               }

               VectorOfERStat[] regionVecs = new VectorOfERStat[channels.Length];
               for (int i = 0; i < regionVecs.Length; i++)
                  regionVecs[i] = new VectorOfERStat();

               try
               {
                  for (int i = 0; i < channels.Length; i++)
                  {
                     er1.Run(channels[i], regionVecs[i]);
                     er2.Run(channels[i], regionVecs[i]);
                  }
                  using (VectorOfUMat vm = new VectorOfUMat(channels))
                  {
                     regions = ERFilter.ERGrouping(image, vm, regionVecs, ERFilter.GroupingMethod.OrientationHoriz,
                        "trained_classifier_erGrouping.xml", 0.5f);
                  }
               }
               finally
               {
                  foreach (UMat tmp in channels)
                     if (tmp != null)
                        tmp.Dispose();
                  foreach (VectorOfERStat tmp in regionVecs)
                     if (tmp != null)
                        tmp.Dispose();
               }

               Rectangle imageRegion = new Rectangle(Point.Empty, imageColor.Size);
               for (int i = 0; i < regions.Length; i++)
               {
                  Rectangle r = ScaleRectangle(regions[i], 1.1);

                  r.Intersect(imageRegion);
                  regions[i] = r;
               }

            }


            List<Tesseract.Character> allChars = new List<Tesseract.Character>();
            String allText = String.Empty;
            foreach (Rectangle rect in regions)
            {
               using (Mat region = new Mat(image, rect))
               {
                  ocr.Recognize(region);
                  Tesseract.Character[] characters = ocr.GetCharacters();

                  //convert the coordinates from the local region to global
                  for (int i = 0; i < characters.Length; i++)
                  {
                     Rectangle charRegion = characters[i].Region;
                     charRegion.Offset(rect.Location);
                     characters[i].Region = charRegion;

                  }
                  allChars.AddRange(characters);

                  allText += ocr.GetText() + Environment.NewLine;

               }
            }

            Bgr drawRegionColor = new Bgr(Color.Red);
            foreach (Rectangle rect in regions)
            {
               CvInvoke.Rectangle(imageColor, rect, drawRegionColor.MCvScalar);
            }
            foreach (Tesseract.Character c in allChars)
            {
               CvInvoke.Rectangle(imageColor, c.Region, drawCharColor.MCvScalar);
            }

            return allText;

         }

      }

      private void OcrImage(Mat source)
      {
         imageBox1.Image = null;
         ocrTextBox.Text = String.Empty;
         hocrTextBox.Text = String.Empty;
         try
         {
            
            Mat result = new Mat();
            String ocredText = OcrImage(_ocr, source, Mode, result);
            imageBox1.Image = result;
            ocrTextBox.Text = ocredText;
            if (Mode == OCRMode.FullPage)
            {
               hocrTextBox.Text = _ocr.GetHOCRText();
            }
         }

         catch (Exception exception)
         {
            MessageBox.Show(exception.Message);
         }
      }

      private void loadImageButton_Click(object sender, EventArgs e)
      {
         if (openImageFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
         {
            fileNameTextBox.Text = openImageFileDialog.FileName;
            Mat source = new Mat(fileNameTextBox.Text);
            OcrImage(source);
         }
      }

      private void loadLanguageToolStripMenuItem_Click(object sender, EventArgs e)
      {
         if (openLanguageFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
         {
            string path = Path.GetDirectoryName(openLanguageFileDialog.FileName);
            string lang = Path.GetFileNameWithoutExtension(openLanguageFileDialog.FileName).Split('.')[0];

            InitOcr(path, lang, OcrEngineMode.Default);

         }
      }
   }
}