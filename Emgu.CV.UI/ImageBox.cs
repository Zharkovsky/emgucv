using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.Reflection;
using System.Diagnostics;
using Emgu.CV;
using Emgu.Util;
using Emgu.CV.Structure;

namespace Emgu.CV.UI
{
   /// <summary>
   /// An image box is a user control that is similar to picture box, but display Emgu CV IImage and provides enhenced functionalities.
   /// </summary>
   public partial class ImageBox : PictureBox
   {
      #region Private Fileds
      /// <summary>
      /// The image that is setted throught the Image property
      /// </summary>
      private IImage _image;
      /// <summary>
      /// The image that is displayed
      /// </summary>
      private IImage _displayedImage;
      private PropertyDialog _propertyDlg;
      /// <summary>
      /// The zoom scale of the image to be displayed
      /// </summary>
      private double _zoomScale = 1.0;

      /// <summary>
      /// The available zoom levels for the displayed image 
      /// </summary>
      public static double[] ZoomLevels = new double[] { 0.125, 0.25, 0.5, 1.0, 2.0, 4.0, 8.0 };

      private FunctionalModeOption _functionalMode = FunctionalModeOption.Everything;

      /// <summary>
      /// A cache to store the ToolStripMenuItems for different types of images
      /// </summary>
      private static readonly Dictionary<Type, ToolStripMenuItem[]> _typeToToolStripMenuItemsDictionary = new Dictionary<Type, ToolStripMenuItem[]>();

      private List<Operation> _operationLists;

      /// <summary>
      /// timer used for caculating the frame rate
      /// </summary>
      private DateTime _timerStartTime;

      /// <summary>
      /// one of the parameters used for caculating the frame rate
      /// </summary>
      private int _imagesReceivedSinceCounterStart;

      private Point _mouseDownPosition;
      private MouseButtons _mouseDownButton;
      private Point _bufferPoint;

      private static readonly Cursor _defaultCursor = Cursors.Cross;
      #endregion

      /// <summary>
      /// Create a ImageBox
      /// </summary>
      public ImageBox()
         : base()
      {
         InitializeComponent();

         BorderStyle = BorderStyle.Fixed3D;
         SetStyle(
            ControlStyles.OptimizedDoubleBuffer,
            true);
         _operationLists = new List<Operation>();
         SetScrollBarValues();
      }

      #region properties
      /// <summary>
      /// Get or set the functional mode for the ImageBox
      /// </summary>
      [Bindable(false)]
      [Category("Design")]
      [DefaultValue(Emgu.CV.UI.ImageBox.FunctionalModeOption.Everything)]
      public FunctionalModeOption FunctionalMode
      {
         get { return _functionalMode; }
         set
         {
            //hide all menu items
            foreach (ToolStripMenuItem mi in contextMenuStrip1.Items)
               mi.Visible = false;

            if (value == FunctionalModeOption.Everything)
               foreach (ToolStripMenuItem mi in contextMenuStrip1.Items)
                  mi.Visible = true;

            _functionalMode = value;
         }
      }

      //private bool _settingBitmap = false;

      private bool EnablePropertyPanel
      {
         get
         {
            return _propertyDlg != null;
         }
         set
         {
            if (value)
            {   //this is a call to enable the property dlg
               if (_propertyDlg == null)
                  _propertyDlg = new PropertyDialog(this);

               _propertyDlg.Show();

               _propertyDlg.FormClosed +=
                   delegate(object sender, FormClosedEventArgs e)
                   {
                      _propertyDlg = null;
                   };

               ImagePropertyPanel.SetOperations(_operationLists);

               // reset the image such that the property is updated
               Image = Image;
            }
            else
            {
               if (_propertyDlg != null)
               {
                  _propertyDlg.Focus();
               }
            }
         }
      }

      /// <summary>
      /// Set the image for this image box
      /// </summary>
      [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
      public new IImage Image
      {
         get
         {
            return _image;
         }
         set
         {
            if (InvokeRequired)
            {
               this.Invoke(new MethodInvoker(delegate() { Image = value; }));
            }
            else
            {
               _image = value;

               IImage imageToBeDisplayed = _image;

               if (imageToBeDisplayed != null)
               {
                  #region perform operations on _operationList if there is any
                  if (_operationLists.Count > 0)
                  {
                     bool isCloned = false;

                     foreach (Operation operation in _operationLists)
                     {
                        if (operation.Method.ReturnType == typeof(void))
                        {  //if this is an inplace operation 

                           if (!isCloned)
                           {  //make a clone if not already done so
                              imageToBeDisplayed = _image.Clone() as IImage;
                              isCloned = true;
                           }

                           //execute the inplace operation
                           operation.InvokeMethod(imageToBeDisplayed);
                        }
                        else if (operation.Method.ReturnType.GetInterface("IImage") != null)
                        {  //if this operation has return value

                           IImage tmp = null;
                           if (isCloned == true) //if intermediate image exist, keep a reference of it
                              tmp = imageToBeDisplayed;

                           isCloned = true;
                           imageToBeDisplayed = operation.InvokeMethod(imageToBeDisplayed) as IImage;

                           if (tmp != null) //dispose the intermediate image
                              tmp.Dispose();
                        }
                        else
                        {
                           throw new System.NotImplementedException(string.Format("Return type of {0} is not implemented.", operation.Method.ReturnType));
                        }
                     }
                  }
                  #endregion

                  DisplayedImage = imageToBeDisplayed;
               }

               operationsToolStripMenuItem.Enabled = (_image != null);
            }
         }
      }

      /// <summary>
      /// Get or Set the zoom scale
      /// </summary>
      [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
      public double ZoomScale
      {
         get
         {
            return _zoomScale;
         }
      }

      /// <summary>
      /// Set the new zoom scale for the displayed image
      /// </summary>
      /// <param name="zoomScale">The new zoom scale</param>
      /// <param name="fixPoint">The point to be fixed</param>
      public void SetZoomScale(double zoomScale, Point fixPoint)
      {
         if (_zoomScale != zoomScale //the scale has been changed
            && //and, the scale is not too small
            !(zoomScale < _zoomScale &&
               (_displayedImage.Size.Width * zoomScale < (2.0 + verticalScrollBar.Width)
               || _displayedImage.Size.Height * zoomScale < (2.0 + horizontalScrollBar.Height)))
            && //and, the scale is not too big
            !(zoomScale > _zoomScale &&
               (ClientSize.Width < zoomScale * 2
               || ClientSize.Height < zoomScale * 2)))
         {
            fixPoint.X = Math.Min(fixPoint.X, (int)(_displayedImage.Size.Width * _zoomScale));
            fixPoint.Y = Math.Min(fixPoint.Y, (int)(_displayedImage.Size.Height * _zoomScale));

            int shiftX = (int)(fixPoint.X * (zoomScale - _zoomScale) / zoomScale / _zoomScale);
            int shiftY = (int)(fixPoint.Y * (zoomScale - _zoomScale) / zoomScale / _zoomScale);

            _zoomScale = zoomScale;

            horizontalScrollBar.Maximum = Int32.MaxValue;
            verticalScrollBar.Maximum = Int32.MaxValue;
            horizontalScrollBar.Value = Math.Min(Math.Max(horizontalScrollBar.Minimum, (int)(horizontalScrollBar.Value + shiftX)), horizontalScrollBar.Maximum);
            verticalScrollBar.Value = Math.Min(Math.Max(verticalScrollBar.Minimum, (int)(verticalScrollBar.Value + shiftY)), verticalScrollBar.Maximum);

            RenderImage();

            if (_propertyDlg != null)
               _propertyDlg.ImagePropertyPanel.UpdateZoomScale();
         }
      }

      private void RenderImage()
      {
         int width = (int)(base.Image.Size.Width * _zoomScale);
         int height = (int)(base.Image.Size.Height * _zoomScale);

         if (width <= Width && height <= Height)
         {  //no ROI is required           
            verticalScrollBar.Visible = false;
            horizontalScrollBar.Visible = false;
         }
         else
         {
            SetScrollBarValues();
         }
         Invalidate();
      }

      /// <summary>
      /// The image that is being displayed. It's the Image following by some user defined image operation
      /// </summary>
      [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
      public IImage DisplayedImage
      {
         get
         {
            return _displayedImage;
         }
         set
         {
            _displayedImage = value;
            if (_displayedImage != null)
            {
               if (_functionalMode != FunctionalModeOption.Minimum)
                  BuildOperationMenuItem(_displayedImage);

               //release the old Bitmap Image               
               if (base.Image != null)
               {
                  base.Image.Dispose();
                  base.Image = null;
               }

               base.Image = _displayedImage.Bitmap;

               RenderImage();
               //SetScrollBarValues();

               if (EnablePropertyPanel)
               {
                  ImagePropertyPanel.SetImage(_displayedImage);

                  #region calculate the frame rate
                  TimeSpan ts = DateTime.Now.Subtract(_timerStartTime);
                  if (ts.TotalSeconds > 1)
                  {
                     ImagePropertyPanel.FramesPerSecondText = _imagesReceivedSinceCounterStart;
                     //reset the counter
                     _timerStartTime = DateTime.Now;
                     _imagesReceivedSinceCounterStart = 0;
                  }
                  else
                  {
                     _imagesReceivedSinceCounterStart++;
                  }
                  #endregion
               }
            }
         }
      }
      #endregion

      /// <summary>
      /// Push the specific operation to the operation collection
      /// </summary>
      /// <param name="operation">The operation to be pushed onto the operation collection</param>
      public void PushOperation(Operation operation)
      {
         _operationLists.Add(operation);
         ImageProperty panel = ImagePropertyPanel;
         if (panel != null)
            panel.SetOperations(_operationLists);

         try
         {
            Image = Image;
         }
         catch
         {  //if pushing the operation generate exceptions
            PopOperation();
            throw; //rethrow the exception
         }
      }

      /// <summary>
      /// Remove all the operations from the collection
      /// </summary>
      public void ClearOperation()
      {
         _operationLists.Clear();
         ImageProperty panel = ImagePropertyPanel;
         if (panel != null) panel.SetOperations(_operationLists);
         Image = Image;
      }

      /// <summary>
      /// Remove the last added operation
      /// </summary>
      public void PopOperation()
      {
         if (_operationLists.Count > 0)
         {
            _operationLists.RemoveAt(_operationLists.Count - 1);
            ImageProperty panel = ImagePropertyPanel;
            if (panel != null) panel.SetOperations(_operationLists);
            Image = Image;
         }
      }

      private ToolStripMenuItem[] BuildOperationTree(IEnumerable<KeyValuePair<string, MethodInfo>> catelogMiPairList)
      {
         List<ToolStripMenuItem> res = new List<ToolStripMenuItem>();

         SortedDictionary<String, List<KeyValuePair<String, MethodInfo>>> catelogDic = new SortedDictionary<string, List<KeyValuePair<String, MethodInfo>>>();
         SortedDictionary<String, MethodInfo> operationItem = new SortedDictionary<string, MethodInfo>();

         foreach (KeyValuePair<string, MethodInfo> pair in catelogMiPairList)
         {
            if (String.IsNullOrEmpty(pair.Key))
            {  //this is an operation
               operationItem.Add(pair.Value.ToString(), pair.Value);
            }
            else
            {  //this is a category
               int index = pair.Key.IndexOf('|');
               String category;
               String subcategory;
               if (index == -1)
               {  //There is no sub category
                  category = pair.Key;
                  subcategory = string.Empty;
               }
               else
               {  //There are sub categories
                  category = pair.Key.Substring(0, index);
                  subcategory = pair.Key.Substring(index + 1, pair.Key.Length - index - 1);
               }

               if (!catelogDic.ContainsKey(category))
                  catelogDic.Add(category, new List<KeyValuePair<String, MethodInfo>>());
               catelogDic[category].Add(new KeyValuePair<String, MethodInfo>(subcategory, pair.Value));
            }
         }

         #region Add catelogs to the menu
         foreach (String catelog in catelogDic.Keys)
         {
            ToolStripMenuItem catelogMenuItem = new ToolStripMenuItem();
            catelogMenuItem.Text = catelog;
            catelogMenuItem.DropDownItems.AddRange(BuildOperationTree(catelogDic[catelog]));
            res.Add(catelogMenuItem);
         }
         #endregion

         #region Add Methods to the menu
         foreach (MethodInfo mi in operationItem.Values)
         {
            ToolStripMenuItem operationMenuItem = new ToolStripMenuItem();
            operationMenuItem.Size = new System.Drawing.Size(152, 22);

            Type[] genericArgs = mi.GetGenericArguments();

            String genericArgString = genericArgs.Length > 0 ?
               String.Format(
                  "<{0}>",
                  String.Join(",", Array.ConvertAll<Type, String>(
                     genericArgs,
                     System.Convert.ToString)))
               : string.Empty;

            operationMenuItem.Text = String.Format(
               "{0}{1}({2})",
               mi.Name,
               genericArgString,
               String.Join(",", System.Array.ConvertAll<ParameterInfo, String>(mi.GetParameters(), delegate(ParameterInfo pi) { return pi.Name; })));

            //This is necessary to handle delegate with a loop
            //Cause me lots of headache before reading the article on
            //http://decav.com/blogs/andre/archive/2007/11/18/wtf-quot-problems-quot-with-anonymous-delegates-linq-lambdas-and-quot-foreach-quot-or-quot-for-quot-loops.aspx
            //I wishes MSFT handle this better
            MethodInfo methodInfoRef = mi;

            operationMenuItem.Click += delegate(Object o, EventArgs e)
                {
                   Object[] paramList = null;

                   while (true)
                   {
                      //Get the parameters for the method
                      //this pop up an input dialog and ask for user input
                      paramList = ParameterInputDialog.GetParams(methodInfoRef, paramList);

                      if (paramList == null) break; //user click cancel on the input dialog

                      //create an operation from the specific methodInfo and parameterlist
                      Operation operation = new Operation(methodInfoRef, paramList);
                      try
                      {
                         PushOperation(operation);
                         break;
                      }
                      catch (Exception expt)
                      {
                         MessageBox.Show((expt.InnerException ?? expt).Message);

                         //special case, then there is no parameter and the method throw an exception
                         //break the loop
                         if (methodInfoRef.GetParameters().Length == 0)
                            break;
                      }
                   }
                };
            res.Add(operationMenuItem);
         }
         #endregion

         return res.ToArray();
      }

      private void BuildOperationMenuItem(IImage image)
      {
         Type typeOfImage = image.GetType();

         operationsToolStripMenuItem.DropDownItems.Clear();

         //check if the menu for the specific image type has been built before
         if (!_typeToToolStripMenuItemsDictionary.ContainsKey(typeOfImage))
         {
            //if not built, build it and save to the cache.
            _typeToToolStripMenuItemsDictionary.Add(
               typeOfImage,
               BuildOperationTree(Reflection.ReflectIImage.GetImageMethods(image))
               );
         }

         operationsToolStripMenuItem.DropDownItems.AddRange(_typeToToolStripMenuItemsDictionary[typeOfImage]);
      }

      private void loadImageToolStripMenuItem_Click(object sender, EventArgs e)
      {
         if (loadImageFromFileDialog.ShowDialog() == DialogResult.OK)
            try
            {
               String filename = loadImageFromFileDialog.FileName;
               Image = new Image<Bgr, byte>(filename);
            }
            catch (Exception excpt)
            {
               MessageBox.Show(excpt.Message);
            }
      }

      private void saveAsToolStripMenuItem_Click(object sender, EventArgs e)
      {
         if (saveImageToFileDialog.ShowDialog() == DialogResult.OK)
            try
            {
               DisplayedImage.Save(saveImageToFileDialog.FileName);
            }
            catch (Exception excpt)
            {
               MessageBox.Show(excpt.Message);
            }
      }

      private void propertyToolStripMenuItem_Click(object sender, EventArgs e)
      {
         EnablePropertyPanel = !EnablePropertyPanel;
      }

      private ImageProperty ImagePropertyPanel
      {
         get
         {
            return EnablePropertyPanel ? _propertyDlg.ImagePropertyPanel : null;
         }
      }

      private void ReverseRectangle()
      {
         if (!_bufferPoint.IsEmpty)
         {
            Rectangle rect = GetSelectedRectangle(_bufferPoint.X, _bufferPoint.Y);
            rect.Location = PointToScreen(rect.Location);
            ControlPaint.DrawReversibleFrame(
               rect,
               Color.White,
               FrameStyle.Dashed);
            _bufferPoint = Point.Empty;
         }
      }

      private Rectangle GetSelectedRectangle(int x, int y)
      {
         int top = Math.Min(y, _mouseDownPosition.Y);
         int bottom = Math.Max(y, _mouseDownPosition.Y);
         int left = Math.Min(x, _mouseDownPosition.X);
         int right = Math.Max(x, _mouseDownPosition.X);

         Rectangle rect = new Rectangle(left, top, right - left, bottom - top);
         rect.Intersect(new Rectangle(Point.Empty, ClientSize));

         return rect;
      }

      /// <summary>
      /// Used for tracking the mouse position on the image
      /// </summary>
      /// <param name="sender"></param>
      /// <param name="e"></param>
      private void ImageBox_MouseMove(object sender, MouseEventArgs e)
      {
         int offsetX = (int)(e.Location.X / _zoomScale);
         int offsetY = (int)(e.Location.Y / _zoomScale);

         if (EnablePropertyPanel)
         {
            int horizontalScrollBarValue = horizontalScrollBar.Visible ? (int)horizontalScrollBar.Value : 0;

            int verticalScrollBarValue = verticalScrollBar.Visible ? (int)verticalScrollBar.Value : 0;

            ImagePropertyPanel.SetMousePositionOnImage(new Point(
               offsetX + horizontalScrollBarValue,
               offsetY + verticalScrollBarValue));
         }

         if (_mouseDownButton == MouseButtons.Middle && (horizontalScrollBar.Visible || verticalScrollBar.Visible))
         {
            int horizontalShift = (int)((e.X - _mouseDownPosition.X) / _zoomScale);
            int verticalShift = (int)((e.Y - _mouseDownPosition.Y) / _zoomScale);

            if (horizontalShift == 0 && verticalShift == 0) return;

            //if (horizontalScrollBar.Visible)
            horizontalScrollBar.Value =
                  Math.Max(Math.Min(horizontalScrollBar.Value - horizontalShift, horizontalScrollBar.Maximum), horizontalScrollBar.Minimum);
            //if (verticalScrollBar.Visible)
            verticalScrollBar.Value =
                  Math.Max(Math.Min(verticalScrollBar.Value - verticalShift, verticalScrollBar.Maximum), verticalScrollBar.Minimum);

            if (horizontalShift != 0) _mouseDownPosition.X = e.Location.X;
            if (verticalShift != 0) _mouseDownPosition.Y = e.Location.Y;

            Invalidate();
         }
         else if (_mouseDownButton == MouseButtons.Left)
         {
            //reverse the previous highlighted rectangle, if there is any
            ReverseRectangle();
            Rectangle rect = GetSelectedRectangle(e.X, e.Y);
            rect.Location = PointToScreen(rect.Location);
            ControlPaint.DrawReversibleFrame(
               rect,
               Color.White,
               FrameStyle.Dashed);
            _bufferPoint = e.Location;
         }
      }


      /// <summary>
      /// The display mode for ImageBox
      /// </summary>
      public enum FunctionalModeOption
      {
         /// <summary>
         /// The ImageBox is only used for displaying image. 
         /// No right-click menu available
         /// </summary>
         Minimum = 0,
         /// <summary>
         /// This is the ImageBox with all functions enabled.
         /// </summary>
         Everything = 1
      }

      #region Handling ScrollBars

      private void HandleScroll(object sender, ScrollEventArgs e)
      {
         Invalidate();
      }

      protected override void OnPaint(PaintEventArgs pe)
      {
         if (base.Image == null)
         {
            OnPaintBackground(pe);
            return;
         }

         Matrix mx = new Matrix((float)_zoomScale, 0, 0, (float) _zoomScale, 0, 0);
         mx.Translate(
            horizontalScrollBar.Visible ? -horizontalScrollBar.Value : 0,
            verticalScrollBar.Visible ? -verticalScrollBar.Value : 0);
         pe.Graphics.Transform = mx;
         pe.Graphics.InterpolationMode = InterpolationMode.High;
         base.OnPaint(pe);
      }

      private void SetScrollBarValues()
      {
         #region determine if the scroll bar should be visible or not
         horizontalScrollBar.Visible = false;
         verticalScrollBar.Visible = false;

         if (_displayedImage != null)
         {
            // If the image is wider than the PictureBox, show the HScrollBar.
            horizontalScrollBar.Visible =
               (int)(_displayedImage.Size.Width * _zoomScale) > ClientSize.Width;

            // If the image is taller than the PictureBox, show the VScrollBar.
            verticalScrollBar.Visible =
               (int)(_displayedImage.Size.Height * _zoomScale) > ClientSize.Height;
         }
         #endregion

         if (base.Image == null) return;

         // Set the Maximum, Minimum, LargeChange and SmallChange properties.
         verticalScrollBar.Minimum = 0;
         horizontalScrollBar.Minimum = 0;

         if (horizontalScrollBar.Visible)
         {  // If the offset does not make the Maximum less than zero, set its value.            
            horizontalScrollBar.Maximum =
               _displayedImage.Size.Width -
               (int)(Math.Max(0, Size.Width - (verticalScrollBar.Visible ? verticalScrollBar.Width : 0)) / _zoomScale);
         }
         else
         {
            horizontalScrollBar.Maximum = 0;
         }

         horizontalScrollBar.LargeChange = (int)Math.Max(horizontalScrollBar.Maximum / 10, 1);
         horizontalScrollBar.SmallChange = (int)Math.Max(horizontalScrollBar.Maximum / 20, 1);

         if (verticalScrollBar.Visible)
         {  // If the offset does not make the Maximum less than zero, set its value.            
            verticalScrollBar.Maximum =
               _displayedImage.Size.Height -
               (int)(Math.Max(0, Size.Height - (horizontalScrollBar.Visible ? horizontalScrollBar.Height : 0)) / _zoomScale);
         }
         else
         {
            verticalScrollBar.Maximum = 0;
         }

         verticalScrollBar.LargeChange = (int)Math.Max(verticalScrollBar.Maximum / 10, 1);
         verticalScrollBar.SmallChange = (int)Math.Max(verticalScrollBar.Maximum / 20, 1);
      }
      #endregion

      void ImageBox_MouseWheel(object sender, System.Windows.Forms.MouseEventArgs e)
      {  //handle the mouse whell scroll (for zooming)
         double scale = 1.0;
         if (e.Delta > 0)
         {
            scale = 2.0;
         }
         else if (e.Delta < 0)
         {
            scale = 0.5;
         }
         else
            return;

         SetZoomScale(ZoomScale * scale, e.Location);
      }

      private void ImageBox_Resize(object sender, EventArgs e)
      {
         if (base.Image != null && ClientSize.Width > 0 && ClientSize.Height > 0)
         {
            RenderImage();
            SetScrollBarValues();
         }
      }

      /// <summary>
      /// Release the this Imagebox and all memory associate with it.
      /// </summary>
      public virtual new void Dispose()
      {
         if (this.Image != null) this.Image.Dispose();
         base.Dispose();
      }

      private void ImageBox_MouseEnter(object sender, EventArgs e)
      {  //set this as the active control 
         Control parent = this.Parent;
         while (!(parent is Form)) parent = parent.Parent;
         (parent as Form).ActiveControl = this;
      }

      private void ImageBox_MouseDown(object sender, MouseEventArgs e)
      {
         _mouseDownPosition = e.Location;
         _mouseDownButton = e.Button;

         _bufferPoint = Point.Empty;
         if (e.Button == MouseButtons.Middle)
            this.Cursor = Cursors.Hand;
      }

      private void ImageBox_MouseUp(object sender, MouseEventArgs e)
      {
         _mouseDownButton = MouseButtons.None;
         this.Cursor = _defaultCursor;

         if (e.Button == MouseButtons.Left)
         {
            ReverseRectangle();

            Rectangle imageRegion = new Rectangle(Point.Empty, ClientSize);
            if (imageRegion.Contains(_mouseDownPosition))
            {
               Rectangle rect = GetSelectedRectangle(e.X, e.Y);
               Point location = rect.Location;
               location = new Point((int)(location.X / _zoomScale), (int)(location.Y / _zoomScale));
               if (horizontalScrollBar.Visible) location.X += horizontalScrollBar.Value;
               if (verticalScrollBar.Visible) location.Y += horizontalScrollBar.Value;

               Size size = new Size((int)(rect.Width / _zoomScale), (int)(rect.Height / _zoomScale));

               if (size.Width > 2 && size.Height > 2)
               {
                  /*
                  _zoomScale = _zoomScale / Math.Max((double)rect.Width / ClientSize.Width, (double)rect.Height / ClientSize.Height);
               
                  horizontalScrollBar.Value = Math.Min(horizontalScrollBar.Maximum, location.X);
                  verticalScrollBar.Value = Math.Min(verticalScrollBar.Maximum, location.Y);
                  RenderImage();*/
               }
            }
         }
      }
   }
}
