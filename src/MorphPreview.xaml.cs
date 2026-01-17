using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace TS4SimRipper
{
    /// <summary>
    /// Interaction logic for MorphPreview.xaml
    /// </summary>
    public partial class MorphPreview : UserControl
    {
        AxisAngleRotation3D rot_x;
        AxisAngleRotation3D rot_y;
        ScaleTransform3D zoom = new ScaleTransform3D(1, 1, 1);
        Transform3DGroup modelTransform, cameraTransform;
        DirectionalLight DirLight1 = new DirectionalLight();
        PointLight PointLight = new PointLight();
        //PerspectiveCamera Camera1 = new PerspectiveCamera();
        OrthographicCamera Camera1 = new OrthographicCamera();
        Model3DGroup modelGroup = new Model3DGroup();
        Viewport3D myViewport = new Viewport3D();
        MaterialGroup myMaterial = new MaterialGroup();

        // Mouse rotation state tracking
        private bool _isRotating = false;
        private Point _lastMousePosition;
        private double _currentXRotation = 0;
        private double _currentYRotation = 0;
        private const double MOUSE_SENSITIVITY = 0.5; // degrees per pixel

        // Mouse panning state tracking
        private bool _isPanning = false;
        private Point _lastPanPosition;
        private const double PAN_SENSITIVITY = 0.005; // camera units per pixel (slower than before)

        public MorphPreview()
        {
            InitializeComponent();
            rot_x = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);
            rot_y = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);

            cameraTransform = new Transform3DGroup();
            cameraTransform.Children.Add(zoom);
            modelTransform = new Transform3DGroup();
            modelTransform.Children.Add(new RotateTransform3D(rot_y));
            modelTransform.Children.Add(new RotateTransform3D(rot_x));

            DirLight1.Color = Colors.White;
            DirLight1.Direction = new Vector3D(.5, -.5, -1);
            PointLight.Color = Colors.DimGray;
            PointLight.Position = new Point3D(1d, 1d, 1d);

            Camera1.FarPlaneDistance = 20;
            Camera1.NearPlaneDistance = 0.05;
           // Camera1.FieldOfView = 45;
            Camera1.LookDirection = new Vector3D(0, -0.10, -3);
            Camera1.UpDirection = new Vector3D(0, 1, 0);
            ModelVisual3D modelsVisual = new ModelVisual3D();
            modelsVisual.Content = modelGroup;

            myViewport.Camera = Camera1;
            myViewport.Children.Add(modelsVisual);
            myViewport.Height = 550;
            myViewport.Width = 480;
            myViewport.Camera.Transform = cameraTransform;
            this.canvas1.Children.Insert(0, myViewport);

            // Mouse rotation and zoom event handlers
            myViewport.MouseDown += Viewport_MouseDown;
            myViewport.MouseMove += Viewport_MouseMove;
            myViewport.MouseUp += Viewport_MouseUp;
            myViewport.MouseLeave += Viewport_MouseLeave;
            myViewport.MouseEnter += Viewport_MouseEnter;
            myViewport.MouseWheel += Viewport_MouseWheel;

            Canvas.SetTop(myViewport, 0);
            Canvas.SetLeft(myViewport, 0);
            this.Width = myViewport.Width;
            this.Height = myViewport.Height;
        }

        MeshGeometry3D SimMesh(GEOM simgeom, float yOffset)
        {
            MeshGeometry3D mesh = new MeshGeometry3D();
            Point3DCollection verts = new Point3DCollection();
            Vector3DCollection normals = new Vector3DCollection();
            PointCollection uvs = new PointCollection();
            Int32Collection facepoints = new Int32Collection();
            int indexOffset = 0;

            GEOM g = simgeom;
            GEOM.GeometryState geostate = g.GeometryStates.FirstOrDefault() ?? g.FullMeshGeometryState();

            for (int i = geostate.MinVertexIndex; i < geostate.VertexCount; i++)
            {
                float[] pos = g.getPosition(i);
                verts.Add(new Point3D(pos[0], pos[1] - (yOffset * .5), pos[2]));
                float[] norm = g.getNormal(i);
                normals.Add(new Vector3D(norm[0], norm[1], norm[2]));
                float[] uv = g.getUV(i, 0);
                uvs.Add(new Point(uv[0], uv[1]));
            }

            for (int i = geostate.StartFace; i < geostate.PrimitiveCount; i++)
            {
                int[] face = g.getFaceIndices(i+ geostate.MinVertexIndex);
                facepoints.Add(face[0] + indexOffset);
                facepoints.Add(face[1] + indexOffset);
                facepoints.Add(face[2] + indexOffset);
            }

            indexOffset += geostate.VertexCount;

            mesh.Positions = verts;
            mesh.TriangleIndices = facepoints;
            mesh.Normals = normals;
            mesh.TextureCoordinates = uvs;
            return mesh;
        }

        internal ImageBrush GetImageBrush(System.Drawing.Image image)
        {
            BitmapImage bmpImg = new BitmapImage();
            MemoryStream ms = new MemoryStream();
            image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            bmpImg.BeginInit();
            bmpImg.StreamSource = ms;
            bmpImg.EndInit();
            ImageBrush img = new ImageBrush();
            img.ImageSource = bmpImg;
            img.Stretch = Stretch.Fill;
            img.TileMode = TileMode.None;
            img.ViewportUnits = BrushMappingMode.Absolute;
            return img;
        }

        //public void Start_Mesh(GEOM model, System.Drawing.Image texture, System.Drawing.Image specular,
        //    GEOM glassModel, System.Drawing.Image glassTexture, System.Drawing.Image glassSpecular, bool setView)
        public void Start_Mesh(GEOM[] model, GEOM[] glass, GEOM[] wings, System.Drawing.Image texture, System.Drawing.Image specular,
            System.Drawing.Image glassTexture, System.Drawing.Image glassSpecular, System.Drawing.Image wingTexture, System.Drawing.Image wingSpecular, bool setView, bool glassIsSeparate)
        {
            Cursor = Cursors.Arrow;
            myMaterial.Children.Clear();
            myMaterial.Children.Add(new DiffuseMaterial(GetImageBrush(texture)));
            myMaterial.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)), 25d));
            if (specular != null) myMaterial.Children.Add(new SpecularMaterial(GetImageBrush(specular), 25d));

            modelGroup.Children.Clear();
            modelGroup.Children.Add(DirLight1);
            modelGroup.Children.Add(PointLight);

            float modelHeight = 0;
            float modelDepth = 0;
            foreach (GEOM geom in model)
            {
                if (geom != null)
                {
                    float[] tmp = geom.GetHeightandDepth();
                    modelHeight = Math.Max(tmp[0], modelHeight);
                    modelDepth = Math.Max(tmp[1], modelDepth);
                }
            }

            if (setView)
            {
                Camera1.Position = new Point3D(0, 0, modelHeight + (modelDepth * 2));
                sliderXMove.Value = 0;
                sliderYMove.Value = 0;
                sliderZoom.Value = -(modelHeight + (modelDepth * 2));
            }

            for (int i = model.Length - 1; i >= 0; i--)
            {
                if (model[i] != null)
                {
                    MeshGeometry3D myBody = SimMesh(model[i], modelHeight);
                    GeometryModel3D myBodyMesh = new GeometryModel3D(myBody, myMaterial);
                    myBodyMesh.Transform = modelTransform;
                    modelGroup.Children.Add(myBodyMesh);
                }
            }

            if (glassIsSeparate && glassTexture != null)
            {
                MaterialGroup glassMaterial = new MaterialGroup();
                glassMaterial.Children.Add(new DiffuseMaterial(GetImageBrush(glassTexture)));
                if (glassSpecular != null) myMaterial.Children.Add(new SpecularMaterial(GetImageBrush(glassSpecular), 25d));
                for (int i = glass.Length - 1; i >= 0; i--)
                {
                    if (glass[i] != null)
                    {
                        MeshGeometry3D myBody = SimMesh(glass[i], modelHeight);
                        GeometryModel3D myBodyMesh = new GeometryModel3D(myBody, glassMaterial);
                        myBodyMesh.Transform = modelTransform;
                        modelGroup.Children.Add(myBodyMesh);
                    }
                }
            }
            else
            {
                for (int i = glass.Length - 1; i >= 0; i--)
                {
                    if (glass[i] != null)
                    {
                        MeshGeometry3D myBody = SimMesh(glass[i], modelHeight);
                        GeometryModel3D myBodyMesh = new GeometryModel3D(myBody, myMaterial);
                        myBodyMesh.Transform = modelTransform;
                        modelGroup.Children.Add(myBodyMesh);
                    }
                }
            }
            if (wingTexture != null)
            {
                MaterialGroup wingsMaterial = new MaterialGroup();
                wingsMaterial.Children.Add(new DiffuseMaterial(GetImageBrush(wingTexture)));
                if (wingSpecular != null) wingsMaterial.Children.Add(new SpecularMaterial(GetImageBrush(wingSpecular), 25d));
                for (int i = wings.Length - 1; i >= 0; i--)
                {
                    if (wings[i] != null)
                    {
                        MeshGeometry3D myBody = SimMesh(wings[i], modelHeight);
                        GeometryModel3D myBodyMesh = new GeometryModel3D(myBody, wingsMaterial);
                        myBodyMesh.Transform = modelTransform;
                        modelGroup.Children.Add(myBodyMesh);
                    }
                }
            }
        }

        public void Stop_Mesh()
        {
            modelGroup.Children.Clear();
        }

        private void sliderZoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            //zoom = new ScaleTransform3D(-sliderZoom.Value / 3, -sliderZoom.Value / 3, -sliderZoom.Value / 3, 0, 0, 0);
            //cameraTransform = new Transform3DGroup();
            //cameraTransform.Children.Add(zoom);
            //Camera1.Transform = cameraTransform;
            Camera1.Position = new Point3D(Camera1.Position.X, Camera1.Position.Y, -sliderZoom.Value);
            Camera1.Width = -sliderZoom.Value;
        }

        private void sliderYMove_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Camera1.Position = new Point3D(Camera1.Position.X, -sliderYMove.Value, Camera1.Position.Z);
        }

        private void sliderXMove_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Camera1.Position = new Point3D(-sliderXMove.Value, Camera1.Position.Y, Camera1.Position.Z);
        }

        private void sliderYRot_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            rot_y.Angle = sliderYRot.Value;
            _currentYRotation = sliderYRot.Value; // Keep mouse tracking in sync
        }

        private void sliderXRot_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            rot_x.Angle = sliderXRot.Value;
            _currentXRotation = sliderXRot.Value; // Keep mouse tracking in sync
        }

        private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isRotating = true;
                _lastMousePosition = e.GetPosition(myViewport);
                myViewport.CaptureMouse(); // Ensures events continue if cursor leaves viewport
                myViewport.Cursor = Cursors.SizeAll;
                e.Handled = true;
            }
            else if (e.MiddleButton == MouseButtonState.Pressed)
            {
                _isPanning = true;
                _lastPanPosition = e.GetPosition(myViewport);
                myViewport.CaptureMouse(); // Ensures events continue if cursor leaves viewport
                myViewport.Cursor = Cursors.Hand;
                e.Handled = true;
            }
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isRotating)
            {
                Point currentPosition = e.GetPosition(myViewport);
                double deltaX = currentPosition.X - _lastMousePosition.X;
                double deltaY = currentPosition.Y - _lastMousePosition.Y;

                // Horizontal movement rotates around Y-axis, vertical around X-axis
                _currentYRotation += deltaX * MOUSE_SENSITIVITY;
                _currentXRotation += deltaY * MOUSE_SENSITIVITY; // Inverted: drag down = rotate down

                // Normalize angles to -180 to 180 range
                _currentYRotation = NormalizeAngle(_currentYRotation);
                _currentXRotation = NormalizeAngle(_currentXRotation);

                // Apply rotations
                rot_y.Angle = _currentYRotation;
                rot_x.Angle = _currentXRotation;

                // Update sliders to reflect current rotation (two-way sync)
                sliderYRot.Value = _currentYRotation;
                sliderXRot.Value = _currentXRotation;

                _lastMousePosition = currentPosition;
                e.Handled = true;
            }
            else if (_isPanning)
            {
                Point currentPosition = e.GetPosition(myViewport);
                double deltaX = currentPosition.X - _lastPanPosition.X;
                double deltaY = currentPosition.Y - _lastPanPosition.Y;

                // Move camera based on mouse delta (inverted both axes)
                // Drag right = pan left, Drag down = pan up (both inverted)
                double newXMove = sliderXMove.Value + (deltaX * PAN_SENSITIVITY); // Inverted: drag right = pan left
                double newYMove = sliderYMove.Value - (deltaY * PAN_SENSITIVITY); // Inverted: drag down = pan up

                // Clamp to slider limits
                newXMove = Math.Max(-2.5, Math.Min(2.5, newXMove));
                newYMove = Math.Max(-3, Math.Min(3, newYMove));

                // Update sliders (which will trigger position update)
                sliderXMove.Value = newXMove;
                sliderYMove.Value = newYMove;

                _lastPanPosition = currentPosition;
                e.Handled = true;
            }
        }

        private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isRotating && e.LeftButton == MouseButtonState.Released)
            {
                _isRotating = false;
                myViewport.ReleaseMouseCapture();
                myViewport.Cursor = Cursors.Arrow; // Restore default
                e.Handled = true;
            }
            else if (_isPanning && e.MiddleButton == MouseButtonState.Released)
            {
                _isPanning = false;
                myViewport.ReleaseMouseCapture();
                myViewport.Cursor = Cursors.Arrow; // Restore default
                e.Handled = true;
            }
        }

        private void Viewport_MouseLeave(object sender, MouseEventArgs e)
        {
            // Mouse capture maintains drag even outside viewport
            // Cleanup happens on MouseUp
        }

        private void Viewport_MouseEnter(object sender, MouseEventArgs e)
        {
            if (!_isRotating && !_isPanning)
            {
                myViewport.Cursor = Cursors.SizeAll; // 4-way arrow rotation indicator
            }
        }

        private double NormalizeAngle(double angle)
        {
            // Keep angle within -180 to 180 range
            while (angle > 180) angle -= 360;
            while (angle < -180) angle += 360;
            return angle;
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Calculate new zoom value based on wheel delta
            // Positive delta = zoom in (decrease zoom value), negative = zoom out (increase zoom value)
            const double ZOOM_SPEED = 0.001; // Adjust sensitivity as needed
            double currentZoom = sliderZoom.Value;
            double zoomChange = e.Delta * ZOOM_SPEED; // Positive: scroll up = zoom in

            double newZoom = currentZoom + zoomChange;

            // Clamp to slider limits (-6 to -0.01)
            newZoom = Math.Max(-6, Math.Min(-0.01, newZoom));

            // Update slider (which will trigger sliderZoom_ValueChanged)
            sliderZoom.Value = newZoom;

            e.Handled = true;
        }

        private void ToggleSlidersButton_Checked(object sender, RoutedEventArgs e)
        {
            // Sliders become visible automatically via binding
        }

        private void ToggleSlidersButton_Unchecked(object sender, RoutedEventArgs e)
        {
            // Sliders become hidden automatically via binding
        }

        private void ResetViewButton_Click(object sender, RoutedEventArgs e)
        {
            // Reset all rotations to front-facing view
            _currentXRotation = 0;
            _currentYRotation = 0;
            rot_x.Angle = 0;
            rot_y.Angle = 0;
            sliderXRot.Value = 0;
            sliderYRot.Value = 0;

            // Reset camera position (panning)
            sliderXMove.Value = 0;
            sliderYMove.Value = 0;

            // Reset zoom to default
            sliderZoom.Value = -3;
        }
    }
}
