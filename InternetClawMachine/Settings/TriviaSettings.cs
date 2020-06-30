using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InternetClawMachine.Settings
{
    public class TriviaSettings
    {
        /// <summary>
        /// Path to the question/anwer json file
        /// </summary>
        public string QuestionsFile { set; get; }

        /// <summary>
        /// Time to wait to "get ready" for the next question in seconds
        /// </summary>
        public int QuestionWaitDelay { set; get; }


    }
}
