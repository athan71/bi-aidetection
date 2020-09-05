﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace AITool
{
    public partial class Frm_CustomMasking : Form
    {
        public Camera cam {get; set;}
        private Bitmap transparentLayer, cameraLayer, inProgessLayer;
        private const string baseDirectory = "./cameras/";
        private const string FILE_TYPE = ".bmp";
        private const float DEFAULT_OPACITY = .5f;
        private int brushSize;

        private PointHistory currentPoints = new PointHistory(); //Contains all points since the mouse down event fired. Draw all points at once in Paint method. Prevents tearing and performance issues
        private List<PointHistory> allPointLists = new List<PointHistory>();  //History of all points. Reserved for future undo feature


        public Frm_CustomMasking()
        {
            InitializeComponent();
            DoubleBuffered = true;
        }

        private void ShowImage()
        {
            try
            {
                //first check for saved image in cameras folder. If doesn't exist load the last camera image.
                if (pbMaskImage.Tag == null || pbMaskImage.Tag.ToString().ToLower() != this.cam.last_image_file.ToLower())
                {
                    if ((!string.IsNullOrWhiteSpace(this.cam.last_image_file)) && (File.Exists(this.cam.last_image_file)))
                    {
                        cameraLayer = new Bitmap(this.cam.last_image_file);

                        //merge layer if masks exist
                        if (File.Exists(baseDirectory + cam.name + FILE_TYPE))
                        {
                            using (Bitmap maskLayer = new Bitmap(baseDirectory + cam.name + FILE_TYPE)) 
                            {
                                pbMaskImage.Image = MergeBitmaps(cameraLayer, maskLayer);
                                transparentLayer = new Bitmap(AdjustImageOpacity(maskLayer,2f)); // create new bitmap here to prevent file locks and increase to 100% opacity
                            }
                        }
                        else //if there are no masks
                        {
                            pbMaskImage.Image = new Bitmap(cameraLayer);
                            transparentLayer = new Bitmap(pbMaskImage.Image.Width, pbMaskImage.Image.Height, PixelFormat.Format32bppPArgb);
                        }
                    }
                }

                pbMaskImage.Refresh();

            }
            catch (Exception ex)
            {
                Global.Log("Error: " + Global.ExMsg(ex));
            }
        }

        private Bitmap MergeBitmaps(Bitmap cameraImage, Bitmap layer)
        {
            Bitmap newImage = new Bitmap(cameraImage.Width, cameraImage.Height, PixelFormat.Format32bppPArgb);

            using (Graphics g = Graphics.FromImage(newImage))
            {
                g.DrawImage(cameraImage, Point.Empty);
                g.DrawImage(layer, Point.Empty);
            }
            return newImage;
        }

        private Point AdjustZoomMousePosition(Point point)
        {
            if (point == null || point.IsEmpty || pbMaskImage.Image == null)
            {
                return point;
            }

            float boxWidth = pbMaskImage.Image.Width;
            float boxHeight = pbMaskImage.Image.Height;
            float imgWidth = pbMaskImage.Width;
            float imgHeight = pbMaskImage.Height;

            //these variables store the padding between image border and picturebox border
            int absX = 0;
            int absY = 0;

            //because the sizemode of the picturebox is set to 'zoom', the image is scaled down
            float scale = 1;

            //Comparing the aspect ratio of both the control and the image itself.
            if (imgWidth / imgHeight > boxWidth / boxHeight) //if the image is p.e. 16:9 and the picturebox is 4:3
            {
                scale = boxWidth / imgWidth; //get scale factor
                absY = (int)(boxHeight - scale * imgHeight) / 2; //padding on top and below the image
            }
            else //if the image is p.e. 4:3 and the picturebox is widescreen 16:9
            {
                scale = boxHeight / imgHeight; //get scale factor
                absX = (int)(boxWidth - scale * imgWidth) / 2; //padding left and right of the image
            }

            int xScaled = (int)(scale * point.X) + absX;
            int yScaled = (int)(scale * point.Y) + absY;

            return new Point(xScaled, yScaled);
        }

        private Bitmap AdjustImageOpacity(Image image, float alphaLevel)
        {
            // Initialize the color matrix.
            // Note the value {level} in row 4, column 4. this is for  the alpha channel
            float[][] matrixItems ={
               new float[] {1, 0, 0, 0, 0},
               new float[] {0, 1, 0, 0, 0},
               new float[] {0, 0, 1, 0, 0},
               new float[] {0, 0, 0, alphaLevel, 0},
               new float[] {0, 0, 0, 0, 1}};
            ColorMatrix colorMatrix = new ColorMatrix(matrixItems);

            // Create an ImageAttributes object and set its color matrix.
            ImageAttributes imageAtt = new ImageAttributes();
            imageAtt.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

            // Now draw the semitransparent bitmap image.
            int iWidth = image.Width;
            int iHeight = image.Height;

            Bitmap newBmp = new Bitmap(image.Width, image.Height);

            using (Graphics g = Graphics.FromImage(newBmp))
            {
                g.DrawImage(
                    image,
                    new Rectangle(0, 0, iWidth, iHeight),  // destination rectangle
                    0.0f,                          // source rectangle x
                    0.0f,                          // source rectangle y
                    iWidth,                        // source rectangle width
                    iHeight,                       // source rectangle height
                    GraphicsUnit.Pixel,
                    imageAtt);
            }
            return newBmp;
        }

        private void Frm_CustomMasking_Load(object sender, EventArgs e)
        {
            Int32.TryParse(numBrushSize.Text, out brushSize);
            ShowImage();
        }

        private void pbMaskImage_Paint(object sender, PaintEventArgs e)
        {
            if (pbMaskImage.Image != null)
            {
                Color color = Color.FromArgb(255,255,255,70);

                if (inProgessLayer == null)
                {
                    inProgessLayer = new Bitmap(transparentLayer.Width, transparentLayer.Height, PixelFormat.Format32bppPArgb);
                }

                //first draw the image for the picturebox. Used as a readonly background layer
                using (Pen pen = new Pen(color, brushSize))
                {
                    pen.MiterLimit = pen.Width / 2;
                    pen.LineJoin = LineJoin.MiterClipped;
                    pen.StartCap = LineCap.Square;
                    pen.EndCap = LineCap.Square;

                    if (currentPoints.GetRectangles().Count > 1)
                    {
                        using (Graphics g = Graphics.FromImage(pbMaskImage.Image))
                        {
                            //first draw the mask on the picturebox. Used as a readonly background layer
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            g.DrawRectangles(pen, currentPoints.GetRectangles().ToArray());
                        }

                        using (Graphics g = Graphics.FromImage(inProgessLayer))
                        {
                            //second draw the mask on a transparent layer. Used as a mask overlay on background defined above.
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            g.DrawRectangles(pen, currentPoints.GetRectangles().ToArray());
                        }
                    }
                }
            }
        }

        private void pbMaskImage_MouseDown(object sender, MouseEventArgs e)
        {
            currentPoints = new PointHistory(AdjustZoomMousePosition(e.Location), brushSize);
        }

        private void pbMaskImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                currentPoints.AddPoint(AdjustZoomMousePosition(e.Location));
                pbMaskImage.Invalidate();
            }
        }

        private void pbMaskImage_MouseUp(object sender, MouseEventArgs e)
        {
            if (inProgessLayer != null)
            {
                transparentLayer = MergeBitmaps(transparentLayer, inProgessLayer);
                pbMaskImage.Image = MergeBitmaps(cameraLayer, AdjustImageOpacity(transparentLayer,DEFAULT_OPACITY));
                inProgessLayer = null;
            }

            if (currentPoints.GetRectangles().Count > 1)
            {
                allPointLists.Add(currentPoints);
                currentPoints = new PointHistory();
            }
        }

        private void numBrushSize_Leave(object sender, EventArgs e)
        {
            if (numBrushSize.Text == "")
            {
                numBrushSize.Text = numBrushSize.Value.ToString();
            }
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            allPointLists.Clear();
            //if mask exists, delete it
            if (File.Exists(baseDirectory + cam.name + FILE_TYPE))
            {
                File.Delete(baseDirectory + cam.name + FILE_TYPE);
            }
            
            ShowImage();
        }

        private void numBrushSize_ValueChanged(object sender, EventArgs e)
        {
            brushSize = (int) numBrushSize.Value;
        }

        private void numBrushSize_KeyUp(object sender, KeyEventArgs e)
        {
            brushSize = (int)numBrushSize.Value;
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            if (transparentLayer != null)
            {
                string path = baseDirectory + cam.name + FILE_TYPE;
                //save masks at 50% opacity 
                AdjustImageOpacity(transparentLayer, DEFAULT_OPACITY).Save(path);
            }
        }


        public class PointHistory
        {
            private List<Rectangle> rectangles = new List<Rectangle>();
            public int brushSize { get; set; }
         
            public PointHistory()
            {
                rectangles = new List<Rectangle>();
                brushSize = 20; 
            }

            public PointHistory(Point point, int brushSize)
            {
                this.rectangles.Add(new Rectangle(point.X, point.Y, brushSize,brushSize));
                this.brushSize = brushSize;
            }

            public void AddPoint(Point point)
            {
                this.rectangles.Add(new Rectangle(point.X, point.Y, brushSize, brushSize));
            }

            public List<Rectangle> GetRectangles()
            {
                return rectangles;
            }

            public void ClearPoints()
            {
                rectangles.Clear();
            }
        }
    }
}
