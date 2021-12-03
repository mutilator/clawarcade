unsigned long _buttonActivatedGameMode = 0;
unsigned long _buttonActivatedBlueButton = 0;
int _buttonDebounceTime = 300;

void checkExternalButtons()
{
    static unsigned long  curTime = 0;
    curTime = millis();
    if (digitalReadFast(PIN_GAME_MODE) == LOW)
    {
        if (curTime - _buttonActivatedGameMode > _buttonDebounceTime)
        {
            if (_currentControllerMode == CONTROLLER_MODE_LOCAL)
                changeGameMode(CONTROLLER_MODE_ONLINE);
            else
                changeGameMode(CONTROLLER_MODE_LOCAL);

        }
        _buttonActivatedGameMode = curTime;
    }

    //local play and button
    int buttonRead = digitalReadFast(PIN_BLUE_BUTTON);
    if (_currentControllerMode == CONTROLLER_MODE_LOCAL)
    {
        if (buttonRead == LOW)
        {
            if (curTime - _buttonActivatedBlueButton > _buttonDebounceTime)
            {
                if (_localModeBallTrackerCount >= _localMaxBalls)
                {
                    startLocalGame();
                } else {
                    _ballReleaseActive = true;
                    releaseBall(-1); //activate the release but don't release it
                }
            }
            _buttonActivatedBlueButton = curTime;
        } else if (_ballReleaseActive && curTime - _buttonActivatedBlueButton > _buttonDebounceTime)
        {
            _ballReleaseActive = false;
            releaseBall(0); //release the ball release
        }
    }
}

void initExternalButtons()
{
    pinMode(PIN_GAME_MODE, INPUT_PULLUP);
    pinMode(PIN_BLUE_BUTTON, INPUT_PULLUP);
}