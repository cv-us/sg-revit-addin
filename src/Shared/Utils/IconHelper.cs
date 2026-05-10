using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace SgRevitAddin.Utils
{
    /// <summary>
    /// Loads embedded PNG icons from the DLL's resources for ribbon buttons.
    ///
    /// Icons are embedded as resources via the .csproj EmbeddedResource glob.
    /// Resource names follow the pattern: {AssemblyName}.Icons.{filename}.png
    ///
    /// USAGE in App.cs:
    ///   var btnData = new PushButtonData(...);
    ///   btnData.LargeImage = IconHelper.LoadIcon("hang-cad-32.png");
    ///   btnData.Image = IconHelper.LoadIcon("hang-cad-16.png");
    ///
    /// HOW TO CHANGE ICONS LATER:
    ///   1. Replace the PNG file in src/Shared/UI/Resources/icons/
    ///   2. Keep the same filename (or update the LoadIcon call in App.cs)
    ///   3. Rebuild and redeploy
    ///   Revit uses 16x16 for small buttons and 32x32 for large buttons.
    ///   Use any image editor (Paint, GIMP, Figma, etc.) to create PNGs.
    /// </summary>
    public static class IconHelper
    {
        /// <summary>
        /// Load an embedded PNG icon by filename.
        /// Returns null if the icon is not found (button will show without an icon).
        /// </summary>
        /// <param name="filename">Just the filename, e.g. "hang-cad-32.png"</param>
        public static BitmapImage LoadIcon(string filename)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string resourceName = $"{assembly.GetName().Name}.Icons.{filename}";

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null) return null;

                var image = new BitmapImage();
                image.BeginInit();
                image.StreamSource = stream;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }
    }
}

