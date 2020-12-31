# Tic-Tac-Toe
- Start tic-tac-toe
- Wait for players to join queue
- After 2 players join start game with those 2 players
    - Allow more players to join queue?
        - no
    - What if a person playing leaves the queue?
        - The queue will be instantly open again for a new person to join
    - If it's first come first serve, how to make it fair for people to play?
        - impossible


# Start Game

- Claw will be over first player own set of pieces they can move and drop the claw
- Upon recoil claw automatically moves over the center of the board
- Player regains control for 20 seconds before the claw automatically drops the ball
    - Possibly turn down power to claw to make timeout longer
    - Check what the default failsafe timeout for the claw is
        - 15 seconds, code brings it to 26 seconds
- Claw returns to player 2's play area
    - This requires resetting the home location after the ball is dropped, either via chat or failsafe (needs to check failsafe event)

Repeat these steps going back and forth between both players


# Todo

All the code - DONE?

When `EVENT_RETURNED_CENTER` is fired during a players turn. Send command to reset home location to the next player. This will return to the proper spot when the player drops or when the failsafe hits - DONE

Add button to UI to crown the winner - DONE

Add command to chat that does the same... - NOT DONE

# ClawSettings

ClawMode: Targeting

Failsafe default: 15 seconds

Player 1 location: FRONTLEFT = 0
Player 2 location: BACKLEFT = 2

# Questions

Check what happens when we're over the play area and failsafe kicks in?
- Open claw
- Wait 250ms
- Return to home automatically
    - Home NEEEDS set on centering by the bot so it should return to the next player home

Can we reset the failsafe for the claw via socket?
- Yes code is done

Check what happens when we drop over play area
- open claw
- wait 1 second
- return home






