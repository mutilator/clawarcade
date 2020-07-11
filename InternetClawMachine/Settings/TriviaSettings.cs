using InternetClawMachine.Games.GameHelpers;
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

        /// <summary>
        /// How do we define the team names?
        /// </summary>
        public TeamNameMode TeamNameMode { set; get; }

        /// <summary>
        /// Predefined list of teams for this game mode
        /// </summary>
        public string[] Teams { set; get; }

        /// <summary>
        /// How many questions from the pool do you want to ask for this round?
        /// </summary>
        public int AvailableQuestions { set; get; }

        /// <summary>
        /// What OBS Scene to show when team 1 wins
        /// </summary>
        public ObsSceneSource OBSCheerTeam1 { set; get; }
        /// <summary>
        /// What OBS Scene to show when team 2 wins
        /// </summary>
        public ObsSceneSource OBSCheerTeam2 { set; get; }

        /// <summary>
        /// What OBS Scene to show when team 1 loses
        /// </summary>
        public ObsSceneSource OBSSadTeam1 { set; get; }
        /// <summary>
        /// What OBS Scene to show when team 2 wins
        /// </summary>
        public ObsSceneSource OBSSadTeam2 { set; get; }

        public ObsSceneSource OBSCorrectAnswer { set; get; }

        /// <summary>
        /// Path to file to load random names from
        /// </summary>
        public string TeamNamesSource { get; set; }

        /// <summary>
        /// How long to wait between giving hints
        /// </summary>
        public int AnswerHintDelay { get; set; }
    }
}
