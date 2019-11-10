using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Resources;
using System.Text;
using System.Text.RegularExpressions;

namespace LocalizationKeyGenerator
{
    public static class MainClass
    {
        private static List<string> Exclusions;
        private static List<string> XamlPropertyList;
        private static Dictionary<string, string> ResourceDictionary;
        private static string ResourceValuePattern = string.Empty;
        private static readonly ReplaceType FileExtension = ReplaceType.xaml;
        private static readonly string FourthWordSelectPattern = @"(?:\S+ ){4}";
        private static readonly string EscapeWhitespaceAndSpecialCharactersPattern = @"[^0-9a-zA-Z]+";
        private static readonly string SearchPath = @"C:\Temp\SampleXamarinProject\Shared\";
        private static readonly string ResourcesPath = @"C:\Temp\SampleXamarinProject\Shared\Globalization\AppResources.resx";
        private static readonly string GlobalizationNamespace = FileExtension == ReplaceType.xaml ? "xmlns:localization=\"clr-namespace:SampleXamarinProject.Globalization\" " : "SampleXamarinProject.Globalization";
        private static string TranslateExtensionPrefix => FileExtension == ReplaceType.xaml ? "{localization:Translate " : "AppResources.";
        private enum ReplaceType
        {
            cs,
            xaml
        }
        private static void Main()
        {
            Exclusions = new List<string> { "AutomationId", "Binding", "Commit", "const", "Contains", "CultureInfo", ".Format", "FromHex", "Parse", "Replace", "MessagingCenter", "OnPropertyChanged", "StyleId", "Value",
                "http", "https", "//", "-->", "&quot;", "-", "$", "&#", ".png", ".pdf", ".svg","JsonProperty"
            };
            XamlPropertyList = new List<string> { "Text", "Title" };
            ResourceDictionary = GetResources();
            switch (FileExtension)
            {
                case ReplaceType.cs:
                    ResourceValuePattern = "(?<=\")(.*)(?=\")";
                    foreach (var file in GetDirectoryList())
                    {
                        FindMatches(file);
                    }
                    break;
                case ReplaceType.xaml:
                    foreach (var file in GetDirectoryList())
                    {
                        foreach (var property in XamlPropertyList)
                        {
                            ResourceValuePattern = $"(?<=\\s{property}=\")([a-z].*)(?=\")";
                            FindMatches(file);
                        }
                    }
                    break;
                default:
                    return;
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
                        line = line.Replace("\"" + existingResourceName + "\"", TranslateExtensionPrefix + replacedValue);
                        if (FileExtension == ReplaceType.xaml)
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
                        line = line.Replace(resourceValue.Value, replacedValue);
                        if (FileExtension == ReplaceType.cs)
                            line = line.Replace("\"" + replacedValue + "\"", replacedValue);
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
                ReplaceType.cs => TranslateExtensionPrefix + filename + "_" + resourceName,
                ReplaceType.xaml => TranslateExtensionPrefix + filename + "_" + Regex.Replace(resourceName, EscapeWhitespaceAndSpecialCharactersPattern, "_") + "}",
                _ => throw new NullReferenceException(),
            };
        }
        private static string[] GetDirectoryList()
        {
            if (Directory.Exists(SearchPath))
            {
                return Directory.GetFiles(SearchPath, $"*.{FileExtension.ToString()}", SearchOption.AllDirectories);
            }
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
            Console.WriteLine("Resource directory not foundç");
            return new Dictionary<string, string>();
        }
        private static void WriteResources(KeyValuePair<string, string> item)
        {
            var fileText = File.ReadAllText(ResourcesPath);
            fileText = fileText.Replace("</root>", $"\n<data name=\"{item.Key}\" xml:space=\"preserve\"><value>{item.Value}</value></data>\n</root>"); //TODO: Format Resource.Designer
            File.WriteAllText(ResourcesPath, fileText);
        }
        private static void WriteClassFile(string file, string finalFile)
        {
            if (!finalFile.Contains(GlobalizationNamespace))
            {
                switch (FileExtension)
                {
                    case ReplaceType.cs:
                        finalFile = "using " + GlobalizationNamespace + ";\n" + finalFile;
                        break;
                    case ReplaceType.xaml:
                        var insertIndex = finalFile.IndexOf("x:Class", StringComparison.Ordinal);
                        finalFile = finalFile.Insert(insertIndex, GlobalizationNamespace);
                        break;
                }
            }
            File.WriteAllText(file, finalFile);
        }
    }
}
