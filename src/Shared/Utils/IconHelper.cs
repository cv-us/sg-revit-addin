using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace SgRevitAddin.Utils
{
    /// <summary>
    /// Loads embedded PNG icons from the DLL's resources for ribbon buttons.
    ///
    /// Icons are embedded as resources via the .csproj EmbeddedResource glob:
    ///   &lt;EmbeddedResource Include="..\Shared\UI\Resources\icons\*.png" LinkBase="Icons" /&gt;
    ///
    /// Resource names follow the .NET SDK convention
    /// "{RootNamespace}.{LinkBase}.{filename}" — for this project that
    /// resolves to "SgRevitAddin.Icons.{filename}" in BOTH the SgRevit24
    /// and SgRevit25 assemblies (RootNamespace = SgRevitAddin in both
    /// csproj files, even though AssemblyName differs).
    ///
    /// HISTORICAL BUG:
    ///   Before commit ee54ee5, this method computed the resource name as
    ///   "{assembly.GetName().Name}.Icons.{filename}" — i.e. it used the
    ///   AssemblyName ("SgRevit24" or "SgRevit25") instead of the
    ///   RootNamespace. Every lookup silently returned null because the
    ///   actual resource was "SgRevitAddin.Icons.{filename}". Result: no
    ///   ribbon button ever showed an icon. The fix is to search the
    ///   manifest by ".Icons.{filename}" suffix instead — resilient to
    ///   RootNamespace renames too.
    ///
    /// USAGE in App.cs:
    ///   var btnData = new PushButtonData(...);
    ///   btnData.LargeImage = IconHelper.LoadIcon("hang-cad-32.png");
    ///   btnData.Image      = IconHelper.LoadIcon("hang-cad-16.png");
    ///
    /// HOW TO CHANGE ICONS LATER:
    ///   1. Replace the PNG file in src/Shared/UI/Resources/icons/
    ///   2. Keep the same filename (or update the LoadIcon call in App.cs)
    ///   3. Rebuild and redeploy
    ///   Revit uses 16x16 for small buttons and 32x32 for large buttons.
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
            if (string.IsNullOrEmpty(filename)) return null;

            Assembly assembly = Assembly.GetExecutingAssembly();

            // Match any resource whose name ends with ".Icons.{filename}".
            // This is resilient to RootNamespace changes — at the time of
            // writing, the resource ends up named
            // "SgRevitAddin.Icons.{filename}".
            string suffix = $".Icons.{filename}";
            string resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.Ordinal));

            if (resourceName == null) return null;

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
