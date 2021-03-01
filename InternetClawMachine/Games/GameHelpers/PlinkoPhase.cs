using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InternetClawMachine.Games.GameHelpers
{
    public enum PlinkoPhase
    {
        /// <summary>
        /// Uh?
        /// </summary>
        NA,
        /// <summary>
        /// When a player is only allowed to press D to drop the crane
        /// </summary>
        GRABBING,
        /// <summary>
        /// When the player is allowed to move left/right and drop the ball in the play area
        /// </summary>
        DROPPING,
        /// <summary>
        /// Waiting for players turn to end before giving control to the next player
        /// </summary>
        WAITING
    }
}
