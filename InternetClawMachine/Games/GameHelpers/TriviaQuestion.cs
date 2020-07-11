using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InternetClawMachine.Games.GameHelpers
{
    public class TriviaQuestion
    {
        /// <summary>
        /// The question to ask
        /// </summary>
        public string Question { set; get; }

        /// <summary>
        /// List of all possible answers for those questions that display the answers
        /// </summary>
        public List<TriviaAnswer> Answers { set; get; }

        /// <summary>
        /// Who correctly answered this question?
        /// </summary>
        public string AnsweredBy { set; get; }

        /// <summary>
        /// Display all answers to the question after the question
        /// </summary>
        public bool ShowAnswers { set; get; }
        public string CorrectAnswer
        {
            get
            {
                foreach (var a in Answers)
                {
                    if (a.IsCorrectAnswer)
                        return a.Text;
                }
                return "";
            }
        }

        /// <summary>
        /// Returns the correct answer from the answer list
        /// </summary>
        public bool IsCorrectAnswer(string answer)
        {
            foreach(var a in Answers)
            {
                if (a.IsCorrectAnswer && a.Equals(answer))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Randomizes all loaded answers
        /// </summary>
        public void RandomizeAnswers()
        {
            Answers.Shuffle();
        }

        internal string getAnswersAsCSV()
        {

            string outp = "";
            Answers.Shuffle();
            foreach(var a in Answers)
            {
                outp += a.Text + ", ";
            }
            return outp.Substring(0, outp.Length - 2);
        }
    }
}
