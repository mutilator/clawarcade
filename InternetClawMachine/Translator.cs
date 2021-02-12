using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace InternetClawMachine
{
    class Translator
    {
        private static List<Localization> Localizations { set; get; }
        public static string _defaultLanguage;
        public static void Init(string configFile)
        {
            //load 
            Localizations = new List<Localization>();
            _defaultLanguage = ""; //set to the first language loaded
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            };
            try
            {
                JsonConvert.PopulateObject(File.ReadAllText(configFile), Localizations);
                _defaultLanguage = Localizations[0].Name;
                Console.WriteLine(Localizations);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        public static string GetTranslation(string reference, string localization)
        {
            var language = Localizations.FirstOrDefault(itm => itm.Name.Equals(localization));
            if (language != null && language.Dictionary.ContainsKey(reference))
            {
                return string.IsNullOrEmpty(language.Dictionary[reference]) ? _defaultLanguage : language.Dictionary[reference];
            }

            language = Localizations.First(itm => itm.Name.Equals(_defaultLanguage));
            if (language.Dictionary.ContainsKey(reference))
            {
                return language.Dictionary[reference];
            }

            //possibly throw exception instead
            return "ERROR NO TRANSLATION: " + reference;
        }

        /// <summary>
        /// Find a given word across all languages provided and return the word in the localization asked for
        /// </summary>
        /// <param name="word">word to find translation for</param>
        /// <param name="localization">language to give the response</param>
        /// <returns>Return the word in the localization asked for, if none is found return the word</returns>
        internal static WordReference FindWord(string word, string localization)
        {
            var w = new WordReference
            {
                SourceWord = word,
                SourceLocalization = localization,
                FinalWord =  word,
                FinalLocalization = localization
            };
                
            foreach (var l in Localizations)
            {
                foreach (var entry in l.Dictionary)
                {
                    if (entry.Value.Equals(word,StringComparison.CurrentCultureIgnoreCase))
                    {
                        w.FinalWord = GetTranslation(entry.Key, localization);
                        w.SourceLocalization = l.Name;
                        return w;
                    }
                }
            }
            return w;
        }
    }

    public class WordReference
    {
        public string SourceWord { set; get; }
        public string SourceLocalization { set; get; }
        public string FinalWord { set; get; }
        public string FinalLocalization { set; get; }
    }

    class Localization
    {
        [JsonProperty("localization")]
        public string Name { set; get; }

        [JsonProperty("dictionary")]
        public Dictionary<string, string> Dictionary { set; get; }

    }

}


