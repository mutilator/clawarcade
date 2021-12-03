unsigned long _scoreSensor1Activated       = 0; //Time the sensor was tripped
unsigned long _scoreSensor2Activated       = 0;
unsigned long _scoreSensor3Activated       = 0;
unsigned long _scoreSensor4Activated       = 0;
unsigned long _scoreSensor5Activated       = 0;
unsigned long _scoreSensor6Activated       = 0;
unsigned long _scoreSensor7Activated       = 0;
unsigned long _scoreSensorBallRtnActivated = 0;
bool _scoreSensor7CanTriggerAgain = true;
int _sensor7GapTime = 0;
unsigned long _sensor7FirstActiveTime = 0;
unsigned long _loopCount = 0;
bool isLow = false;

void initScoring()
{
    pinMode(PIN_SCORE_SENSOR_1, INPUT);
    pinMode(PIN_SCORE_SENSOR_2, INPUT);
    pinMode(PIN_SCORE_SENSOR_3, INPUT);
    pinMode(PIN_SCORE_SENSOR_4, INPUT);
    pinMode(PIN_SCORE_SENSOR_5, INPUT);
    pinMode(PIN_SCORE_SENSOR_6, INPUT);
    pinMode(PIN_SCORE_SENSOR_BALL_STOP, INPUT);
    pinMode(PIN_SCORE_SENSOR_BALL_RTN, INPUT);



    pinMode(PIN_SCORE_ENABLE_1, OUTPUT);
    pinMode(PIN_SCORE_ENABLE_2, OUTPUT);
    pinMode(PIN_SCORE_ENABLE_3, OUTPUT);
    pinMode(PIN_SCORE_ENABLE_4, OUTPUT);
    pinMode(PIN_SCORE_ENABLE_5, OUTPUT);
    pinMode(PIN_SCORE_ENABLE_6, OUTPUT);
    pinMode(PIN_SCORE_ENASBLE_BALL_STOP, OUTPUT);
    pinMode(PIN_SCORE_ENABLE_BALL_RTN, OUTPUT);

}

void setScoring(int scoreSlot, bool isEnabled)
{
    switch (scoreSlot)
    {
        case 1:
            digitalWrite(PIN_SCORE_ENABLE_1, isEnabled);
            _checkScoringSensor1 = isEnabled;
            break;
        case 2:
            digitalWrite(PIN_SCORE_ENABLE_2, isEnabled);
            _checkScoringSensor2 = isEnabled;
            break;
        case 3:
            digitalWrite(PIN_SCORE_ENABLE_3, isEnabled);
            _checkScoringSensor3 = isEnabled;
            break;
        case 4:
            digitalWrite(PIN_SCORE_ENABLE_4, isEnabled);
            _checkScoringSensor4 = isEnabled;
            break;
        case 5:
            digitalWrite(PIN_SCORE_ENABLE_5, isEnabled);
            _checkScoringSensor5 = isEnabled;
            break;
        case 6:
            digitalWrite(PIN_SCORE_ENABLE_6, isEnabled);
            _checkScoringSensor6 = isEnabled;
            break;
        case 7:
            //digitalWrite(PIN_SCORE_ENASBLE_BALL_STOP, isEnabled); //no power is required for this pin
            _checkScoringSensor7 = isEnabled;
            break;
        case 8:
            digitalWrite(PIN_SCORE_ENABLE_BALL_RTN, isEnabled);
            _checkScoringSensor8 = isEnabled;
            break;
        case 9:
            _checkScoringSensor1 = isEnabled;
            _checkScoringSensor2 = isEnabled;
            _checkScoringSensor3 = isEnabled;
            _checkScoringSensor4 = isEnabled;
            _checkScoringSensor5 = isEnabled;
            _checkScoringSensor6 = isEnabled;
            _checkScoringSensor7 = isEnabled;
            _checkScoringSensor8 = isEnabled;
            digitalWrite(PIN_SCORE_ENABLE_1, isEnabled);
            digitalWrite(PIN_SCORE_ENABLE_2, isEnabled);
            digitalWrite(PIN_SCORE_ENABLE_3, isEnabled);
            digitalWrite(PIN_SCORE_ENABLE_4, isEnabled);
            digitalWrite(PIN_SCORE_ENABLE_5, isEnabled);
            digitalWrite(PIN_SCORE_ENABLE_6, isEnabled);
            //digitalWrite(PIN_SCORE_ENASBLE_BALL_STOP, isEnabled); //no power is required for this pin
            digitalWrite(PIN_SCORE_ENABLE_BALL_RTN, isEnabled);
            break;
        default:
            _checkScoringSensor1 = false;
            _checkScoringSensor2 = false;
            _checkScoringSensor3 = false;
            _checkScoringSensor4 = false;
            _checkScoringSensor5 = false;
            _checkScoringSensor6 = false;
            _checkScoringSensor7 = false;
            _checkScoringSensor8 = false;
            break;
    }
    _timestampScoringEnabled = millis(); //we set this so scores arent tallied right away, when turning on sensors some of them will trip
}


void checkScoreSensors()
{
    static unsigned long curTime = 0;
    curTime = millis();
    bool hasFive = false; //five and six combine == the 5000 sensor, use these vars to do a check against both sensors
    bool hasSix = false;

    //Check the ball return stop to see if a ball is passing
    int scorePin7 = digitalReadFast(PIN_SCORE_SENSOR_BALL_STOP);

    if (_checkScoringSensor7 && scorePin7 == _scoreActivated)
    {
        if (!isLow)
        {
            if (_isDebugMode)
            {
                debugString("#### VAL CHANGE - HIGH LOOPS COMPLETED #### ");
                debugInt(_loopCount);
                debugLine("");
                _loopCount = 0;
            }
        }
        _loopCount++;
        isLow = true;
    } else {
        if (isLow)
        {
            if (_isDebugMode)
            {
                debugString("#### VAL CHANGE - LOW LOOPS COMPLETED #### ");
                debugInt(_loopCount);
                debugLine("");
                _loopCount = 0;
            }
        }
        _loopCount++;
        isLow = false;
    }
    if (_checkScoringSensor7 && scorePin7 == _scoreActivated && isLow && _loopCount > 100)
    {
        
        if (_isDebugMode && curTime - 10 > _scoreSensor7Activated)
        {
            _sensor7GapTime += curTime - _scoreSensor7Activated;
            debugString("-------- ACTIVE ---------- ");
            debugLong(curTime);
            debugString(" - ");
            debugInt(curTime - _scoreSensor7Activated);
            debugLine("");
            if (_sensor7GapTime > _ballStopTriggerDuration)
            {
                debugString("-------- GAP TIME TRIGGER ---------- ");
                debugLong(curTime);
                debugString(" - ");
                debugInt(_sensor7GapTime);
                debugLine("");
                _sensor7GapTime = 0;
            }
        }
        
        if (curTime - _ballStopTriggerDuration > _scoreSensor7Activated && _scoreSensor7CanTriggerAgain)
        {
            _sensor7FirstActiveTime = curTime;
            _scoreSensor7CanTriggerAgain = false;
            tallyScore(SCORE_SLOT_BALL_STOP);
        }
        _scoreSensor7Activated = curTime;
    } else if (scorePin7 != _scoreActivated && !_scoreSensor7CanTriggerAgain && !isLow && _loopCount > 100)
    {
        // require that we're not reading activated
        // require that the sensor is set to no trigger allowed
        if (_isDebugMode)
        {
            debugString("-------- DEACTIVATED ---------- ");
            debugLong(curTime);
            debugString(" - ");
            debugLong(curTime - _sensor7FirstActiveTime);
            debugLine("");
        }

        _sensor7GapTime = 0;
        _scoreSensor7CanTriggerAgain = true;
    }

    // We have a delay before we can keep track of any score sensors other than the one above
    if (curTime - _timestampScoringEnabled < _scoreEnableWaitTime)
        return;

    // ####################
    // #      SLOT 1      #
    // ####################

    if (_checkScoringSensor1 && digitalReadFast(PIN_SCORE_SENSOR_1) == _scoreActivated)
    {
        if (curTime - _sensorActivationDelay > _scoreSensor1Activated)
        {
            tallyScore(SCORE_SLOT_1000);
        }
        _scoreSensor1Activated = curTime;
    }

    // ####################
    // #      SLOT 2      #
    // ####################

    if (_checkScoringSensor2 && digitalReadFast(PIN_SCORE_SENSOR_2) == _scoreActivated)
    {
        if (curTime - _sensorActivationDelay > _scoreSensor2Activated)
        {
            tallyScore(SCORE_SLOT_2000);
        }

        _scoreSensor2Activated = curTime;
    }

    // ####################
    // #      SLOT 3      #
    // ####################

    if (_checkScoringSensor3 && digitalReadFast(PIN_SCORE_SENSOR_3) == _scoreActivated)
    {
        if (curTime - _sensorActivationDelay > _scoreSensor3Activated)
        {

            tallyScore(SCORE_SLOT_3000);
        }
        _scoreSensor3Activated = curTime;
    }

    // ####################
    // #      SLOT 4      #
    // ####################

    if (_checkScoringSensor4 && digitalReadFast(PIN_SCORE_SENSOR_4) == _scoreActivated)
    {
        if (curTime - _sensorActivationDelay > _scoreSensor4Activated)
        {

            tallyScore(SCORE_SLOT_4000);
        }
        _scoreSensor4Activated = curTime;
    }

    // ####################
    // #      SLOT 5      #
    // ####################

    if (_checkScoringSensor5 && digitalReadFast(PIN_SCORE_SENSOR_5) == _scoreActivated)
    {
        if (curTime - _sensorActivationDelay > _scoreSensor5Activated)
        {
            debugString(" ---------- SCORE 5 --------- ");
            debugLong(curTime);
            debugLine("");
            hasFive = true;
            unsigned long extraCurTime = millis();
            for(int i = 0; i < _maxSensorRechecks; i++)
            {
                extraCurTime = millis();
                if (_checkScoringSensor6 && digitalReadFast(PIN_SCORE_SENSOR_6) == _scoreActivated)
                {
                    if (extraCurTime - _sensorActivationDelay > _scoreSensor6Activated)
                    {
                        debugString(" ---------- SCORE 6 INTERIOR --------- ");
                        debugLong(extraCurTime);
                        debugString(" - ");
                        debugInt(i);
                        debugLine("");
                        hasSix = true;
                        _scoreSensor6Activated = extraCurTime;
                        break;
                    }
                    _scoreSensor6Activated = extraCurTime;
                }
            }
        }
        _scoreSensor5Activated = curTime;
    }

    // ####################
    // #      SLOT 6      #
    // ####################

    if (_checkScoringSensor6 && digitalReadFast(PIN_SCORE_SENSOR_6) == _scoreActivated)
    {
        if (curTime - _sensorActivationDelay > _scoreSensor6Activated)
        {
            debugString(" ---------- SCORE 6 --------- ");
            debugLong(curTime);
            debugLine("");
            hasSix = true;
            unsigned long extraCurTime = millis();
            for(int i = 0; i < _maxSensorRechecks; i++)
            {
                extraCurTime = millis();
                if (_checkScoringSensor5 && digitalReadFast(PIN_SCORE_SENSOR_5) == _scoreActivated)
                {
                    
                    if (extraCurTime - _sensorActivationDelay > _scoreSensor5Activated)
                    {
                        debugString(" ---------- SCORE 5 INTERIOR --------- ");
                        debugLong(extraCurTime);
                        debugString(" - ");
                        debugInt(i);
                        debugLine("");
                        hasFive = true;
                        _scoreSensor5Activated = extraCurTime;
                        break;
                    }
                    _scoreSensor5Activated = extraCurTime;

                }
            }
        }
        _scoreSensor6Activated = curTime;
    }

    // ########################
    // #      SLOT 5 & 6      #
    // ########################

    if (hasFive && hasSix)
    {
        tallyScore(SCORE_SLOT_5000);
    } else if (hasFive)
    {
        tallyScore(SCORE_SLOT_10K_RIGHT);
    } else if (hasSix)
    {
        tallyScore(SCORE_SLOT_10K_LEFT);
    }


    // ####################
    // #      SLOT 8      #
    // ####################

    if (_checkScoringSensor8 && digitalReadFast(PIN_SCORE_SENSOR_BALL_RTN) == _scoreActivated)
    {
        if (curTime - 200 > _scoreSensorBallRtnActivated)
        {
            tallyScore(SCORE_SLOT_RETURN);
        }
        _scoreSensorBallRtnActivated = curTime;
    }
}

void tallyScore(int scoreSlot)
{
    char strScoreSlot[2];
    itoa(scoreSlot, strScoreSlot, 10);

    sendFormattedResponse(EVENT_SCORE, "0", strScoreSlot);

    if (_currentControllerMode == CONTROLLER_MODE_ONLINE)
    {
        //we only allow one ball at a time in online mode
        if (scoreSlot == SCORE_SLOT_BALL_STOP) //if a ball just went through, stop balls
        {
            //don't let it release again if it's already released, run out the last timer, limits balls released if one slipped thru 
            if (!_ballReleasedRemotely)
                return;

            sendFormattedResponse(EVENT_BALL_RELEASED, "0", "");
            
            releaseBall(_releaseWaitDuration); //update time to let the ball pass over the stop then close it
            _ballReleasedRemotely = false;
        }
        if (scoreSlot == SCORE_SLOT_RETURN)
        {
            sendFormattedResponse(EVENT_BALL_RETURNED, "0", "");
        }
        return;
    }

    handleLocalScoreSlot(scoreSlot);
}

void handleLocalScoreSlot(int scoreSlot)
{
    
    char strScore[7];

    // a simple ball counter for balls that pass the ball return
    if (scoreSlot == SCORE_SLOT_RETURN)
        _localModeBallTrackerCount++;

    // if we haven't let the player have all their balls, enable the ball release to let this one pass
    if (_ballsReleased < _localMaxBalls && scoreSlot == SCORE_SLOT_RETURN)
    {
        releaseBall(4000);
    }

    //slot 7 increments the balls no matter what
    if (scoreSlot == SCORE_SLOT_BALL_STOP)
    {
        _ballsReleased++;

        if (_ballsReleased == _localMaxBalls) //if we reached max, close the release
        {
            releaseBall(_releaseWaitDuration); //update time to let the ball pass over the stop then close it
        }
    }

    //When the last ball goes through the slot that we score for, ignore any further score tally
    if (_localModeBallTrackerCount >= _localMaxBalls)
    {
        return;
    }



    switch (scoreSlot)
    {
        case SCORE_SLOT_1000:
            _localModeCurrentScore+=1000;
            break;
        case SCORE_SLOT_2000:
            _localModeCurrentScore+=2000;
            break;
        case SCORE_SLOT_3000:
            _localModeCurrentScore+=3000;
            break;
        case SCORE_SLOT_4000:
            _localModeCurrentScore+=4000;
            break;
        case SCORE_SLOT_5000:
            _localModeCurrentScore+=5000;
            break;
        case SCORE_SLOT_10K_RIGHT:
            _localModeCurrentScore+=10000;
            break;
        case SCORE_SLOT_10K_LEFT:
            _localModeCurrentScore+=10000;
            break;
        default: //if ball return or ball stop just exit
            return;
        
    }

    ltoa(_localModeCurrentScore, strScore, 10);
    sendDisplayControllerMessage(strScore);
}