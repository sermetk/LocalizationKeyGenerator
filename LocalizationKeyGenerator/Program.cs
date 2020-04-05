using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LocalizationKeyGenerator
{
    public static class MainClass
    {
        private static string ResourceValuePattern;
        private static readonly ReplaceType FileExtension = ReplaceType.xaml;
        private static readonly string FourthWordSelectPattern = @"(?:\S+ ){4}";
        private static readonly string EscapeWhitespaceAndSpecialCharactersPattern = @"[^0-9a-zA-Z]+";
        private static readonly string SearchPath = @"C:\Temp\SampleXamarinProject\Shared\";
        private static readonly string ResourcesPath = @"C:\Temp\SampleXamarinProject\Shared\Globalization\AppResources.resx";
        private static readonly string GlobalizationNamespace = FileExtension == ReplaceType.xaml ? "xmlns:localization=\"clr-namespace:SampleXamarinProject.Globalization\" " : "SampleXamarinProject.Globalization";
        private static readonly string TranslateExtensionPrefix = FileExtension == ReplaceType.xaml ? "{x:Static localization:" : "AppResources.";
        private static readonly Dictionary<string, string> ResourceDictionary = GetResources();
        private static readonly List<string> XamlPropertyList = new List<string> { "Text", "Title", "PlaceHolderText" };
        private static readonly List<string> Exclusions = new List<string> {"&#10;","&quot;",".Format",".pdf",".png",".svg","//","-->",
            "AutomationId","Binding","Commit","const","Contains","CultureInfo","FromHex","http","https","JsonProperty","MessagingCenter",
            "OnPropertyChanged","Parse","Replace","StyleId","Value"};

        private enum ReplaceType
        {
            cs,
            xaml
        }
        private static void Main()
        {
            if (FileExtension == ReplaceType.cs)
            {
                ResourceValuePattern = "(?<=\")(.*)(?=\")";
                foreach (var file in GetDirectoryList())
                {
                    FindMatches(file);
                }
            }
            else if (FileExtension == ReplaceType.xaml)
            {
                foreach (var file in GetDirectoryList())
                {
                    foreach (var property in XamlPropertyList)
                    {
                        ResourceValuePattern = $"(?<=\\s{property}=\")([a-z].*)(?=\")";
                        FindMatches(file);
                    }
                }
            }
            else
            {
                throw new NotImplementedException();
            }
        }
        private static void FindMatches(string file)
        {
            using var sr = new StreamReader(file);
            var finalFile = string.Empty;
            var matchCount = 0;
            do
            {
                var line = sr.ReadLine();
                if (FileExtension == ReplaceType.cs && Exclusions.Any(line.Contains))
                    return;
                var resourceValueGroup = Regex.Matches(line, ResourceValuePattern, RegexOptions.IgnoreCase);
                foreach (Match resourceValue in resourceValueGroup)
                {
                    if (resourceValue.Value == string.Empty)
                        return;
                    matchCount += 1;
                    var replacedValue = string.Join(string.Empty, resourceValue.Value.Normalize(NormalizationForm.FormD).Where(c =>
                            char.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).Replace("ı", "i");
                    if (Regex.IsMatch(replacedValue, FourthWordSelectPattern))
                        replacedValue = Regex.Match(replacedValue, FourthWordSelectPattern).Value;
                    replacedValue = Regex.Replace(replacedValue, EscapeWhitespaceAndSpecialCharactersPattern, string.Empty);
                    if (ResourceDictionary.ContainsValue(resourceValue.Value))
                    {
                        var existingResourceName = ResourceDictionary.FirstOrDefault(x => x.Value == resourceValue.Value).Key;

                        if (FileExtension == ReplaceType.cs)
                            line = line.Replace("\"" + resourceValue.Value + "\"", TranslateExtensionPrefix + replacedValue);
                        else if (FileExtension == ReplaceType.xaml)
                            line = line.Replace(resourceValue.Value, TranslateExtensionPrefix + existingResourceName + "}");

                        Console.WriteLine("Class:{0}", line);
                        Console.ReadKey();
                    }
                    else
                    {
                        var resourceName = Path.GetFileNameWithoutExtension(file) + "_" + replacedValue;
                        replacedValue = GetReplacedResourceName(replacedValue, Path.GetFileNameWithoutExtension(file));
                        ResourceDictionary.Add(resourceName, resourceValue.Value);
                        WriteResources(ResourceDictionary.Last());

                        if (FileExtension == ReplaceType.cs)
                            line = line.Replace("\"" + resourceValue.Value + "\"", replacedValue);
                        else if (FileExtension == ReplaceType.xaml)
                            line = line.Replace(resourceValue.Value, replacedValue);

                        Console.WriteLine("Key: {0}\nValue: {1}", ResourceDictionary.Last().Key, ResourceDictionary.Last().Value);
                        Console.WriteLine("Class:{0}", line);
                        Console.ReadKey();
                    }
                }
                finalFile += line + "\n";
            } while (!sr.EndOfStream);
            sr.Close();
            if (matchCount > 0)
                WriteClassFile(file, finalFile);
        }
        private static string GetReplacedResourceName(string resourceName, string filename)
        {
            return FileExtension switch
            {
                ReplaceType.cs => TranslateExtensionPrefix + filename + "_" + string.Empty + resourceName,
                ReplaceType.xaml => TranslateExtensionPrefix + filename + "_" + Regex.Replace(resourceName, EscapeWhitespaceAndSpecialCharactersPattern, "_") + "}",
                _ => throw new NotImplementedException(),
            };
        }
        private static string[] GetDirectoryList()
        {
            if (Directory.Exists(SearchPath))
                return Directory.GetFiles(SearchPath, $"*.{FileExtension}", SearchOption.AllDirectories);

            Environment.Exit(2);
            return null;
        }
        private static Dictionary<string, string> GetResources()
        {
            if (File.Exists(ResourcesPath))
            {
                var tempDictionary = new Dictionary<string, string>();
                var resources = fmdev.ResX.ResXFile.Read(ResourcesPath);
                foreach (var d in resources)
                {
                    tempDictionary.Add(d.Id, d.Value);
                }
                return tempDictionary;
            }
            Console.WriteLine("Resource directory not found.");
            return new Dictionary<string, string>();
        }
        private static void WriteResources(KeyValuePair<string, string> item)
        {
            var fileText = File.ReadAllText(ResourcesPath);
            fileText = fileText.Replace("</root>", $"\n<data name=\"{item.Key}\" xml:space=\"preserve\"><value>{item.Value}</value></data>\n</root>"); //TO DO: Format Resource.Designer
            File.WriteAllText(ResourcesPath, fileText);
        }
        private static void WriteClassFile(string file, string finalFile)
        {
            if (!finalFile.Contains(GlobalizationNamespace))
            {
                if (FileExtension == ReplaceType.cs)
                {
                    finalFile = "using " + GlobalizationNamespace + ";\n" + finalFile;
                }
                else if (FileExtension == ReplaceType.xaml)
                {
                    var insertIndex = finalFile.IndexOf("x:Class");
                    finalFile = finalFile.Insert(insertIndex, GlobalizationNamespace);
                }
                else
                {
                    throw new NotImplementedException();
                }
            }
            File.WriteAllText(file, finalFile);
        }
    }
}
