using System;

namespace InternetClawMachine.Games.GameHelpers
{
    public class TriviaAnswer
    {
        /// <summary>
        /// The string answer
        /// </summary>
        public string Text { set; get; }

        /// <summary>
        /// Is this the correct answer to a question?
        /// </summary>
        public bool IsCorrectAnswer { set; get; }

        public override bool Equals(object obj)
        {
            // If the passed object is null
            if (obj == null)
            {
                return false;
            }
            if (!(obj is TriviaAnswer))
            {
                if (obj is string s)
                {
                    return string.Equals(Text, s, StringComparison.CurrentCultureIgnoreCase);
                }

                return false;
            }
            return Text.ToLower() == ((TriviaAnswer)obj).Text.ToLower();
        }

        public override int GetHashCode()
        {
            return Text.GetHashCode();
        }

    }
}