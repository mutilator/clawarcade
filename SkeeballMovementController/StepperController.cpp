#include "Arduino.h"
#include "Defines.h"
#include "StepperController.h"
#include "DigitalWriteFast.h"





StepperController::StepperController(int stepPin, int dirPin, int enablePin, int homePin, int endPin)
{
    _running = false; //just to show we have these available
    _runningToEnd = false; //just to show we have these available
    _autoHoming = false; //just to show we have these available
    _stepIndexMode = false; //only set this when we step a set distance

    _direction = DIRECTION_FORWARD; //just to show we have these available


    _acceleration = 20; //default acceleration
    _homed = false;
    _stepPin = stepPin;
    _dirPin = dirPin;
    _homePin = homePin; //default these so we know when they're actually set
    _endPin = endPin; //default these so we know when they're actually set
    _enablePin = enablePin; //default these so we know when they're actually set
    _enabled = true;
    _lastMoveCompleteFlag = false; //flag tells us when we checked to see if the last move completed
    _lastHomeCompleteFlag = false; //flag tells us when we checked to see if the last home completed

    _stepperId = 1;

    _limitSwitchBackoffSteps = 40; //how many steps to backoff when auto homing

    setDisableOnLimit(true); //we want to disable the motor when we hit a limit switch by default
    setLimits(10000l, 0l); //limit movement based on number of steps rather than switches
    
    setMaxSpeed(3000); //400uS pulses

    pinMode(stepPin, OUTPUT);
    pinMode(dirPin, OUTPUT);
    if (enablePin > -1)
        pinMode(enablePin, OUTPUT);

    if (homePin > -1)
        pinMode(homePin, INPUT_PULLUP);
    if (endPin > -1)
        pinMode(endPin, INPUT_PULLUP);

}

void StepperController::setId(int id)
{
    _stepperId = id;
}

int StepperController::getId()
{
    return _stepperId;
}


bool StepperController::getLastMoveCompleted()
{
    if (_lastMoveCompleteFlag)
    {
        _lastMoveCompleteFlag = false;
        return true;
    }
    return false;
}
bool StepperController::getLastHomeCompleted()
{
    if (_lastHomeCompleteFlag)
    {
        _lastHomeCompleteFlag = false;
        return true;
    }
    return false;
}
void StepperController::setMaxSpeed(int speed)
{
    _maxSpeed = speed;
}
void StepperController::calcSpeed()
{
    if (!_running)
        return;
    //ignore deceleration for now
    /*
    if (_stepPosition == _decelStep || _decelerating)
    {
        _decelerating = true;
        _currentSpeed = _currentSpeed - _acceleration; //linear deceleration
        if (_currentSpeed <= 0)
            _currentSpeed = _acceleration;

        _stepDelayTime = (long)((1.0/(float)_currentSpeed) * 1000.0 * 1000.0);
    }*/
    if (_currentSpeed == _maxSpeed)
    {
        //do nothing, speed is set, cruising
    } else { //accelerating
        _currentSpeed = _currentSpeed + _acceleration; //linear acceleration
        if (_currentSpeed > _maxSpeed)
            _currentSpeed = _maxSpeed;
        _testSpeed = (float)_currentSpeed;
        _testDivisor = 1.0/_testSpeed;
        _floatResult = _testDivisor * 1000.0 * 1000.0;
        _stepDelayTime = (long)_floatResult;
    }
}
void StepperController::setEventLimitHome(void (*eventFunction))
{
    _limitSwitchEventHomeFunction = eventFunction;
}

void StepperController::setEventLimitEnd(void (*eventFunction))
{
    _limitSwitchEventEndFunction = eventFunction;
}

void StepperController::setEventMoveComplete(void (*eventFunction))
{
    _moveCompleteEventFunction = eventFunction;
}

bool StepperController::isRunning()
{
    return _running;
}

bool StepperController::isHomed()
{
    return _homed;
}
void StepperController::autoHome()
{
    //make sure home pin is set
    if (_homePin != -1)
    {
        
        this->unsetHome();
        //this tells the step() command we're in autohome mode so once the home pin is tripped we set home
        _autoHoming = true;

        //make sure _direction = false, default _direction is the home pin
        this->setDirection(DIRECTION_REVERSE);
        _decelStep = -1;
        //begin stepping
        this->start();
    }
}

void StepperController::runToEnd()
{
    //make sure home pin is set
    if (_homePin != -1 && _endPin != -1 && _homed)
    {
        //this tells the step() command we're in running to end and should step until we reach a limit
        _runningToEnd = true;
        //make sure _direction = true, default _direction for ending
        this->setDirection(DIRECTION_FORWARD);
        _decelStep = -1;
        //begin stepping
        this->start();
    }
}

void StepperController::setHome()
{
    
    _homed = true;
    _stepPosition = 0l;
}
void StepperController::unsetHome()
{
    _homed = false;
    _stepPosition = 0l;
}
void StepperController::changeDirection()
{
    //if this is running already, we need to reset acceleration stuff
    if (_running)
    {
        _currentSpeed = 0;
    }
    this->setDirection(!_direction);
}

void StepperController::setDirection(bool dir)
{
    _direction = dir;

    if (_direction)
        digitalWrite(_dirPin, DIRECTION_FORWARD?HIGH:LOW); // Move one _direction
    else
        digitalWrite(_dirPin, DIRECTION_FORWARD?LOW:HIGH); // Or another
}

void StepperController::setLimits(long high, long low)
{
    if (high == -1l && low == -1l)
    {
        _disableLimitChecks = true;
    } else {
        _disableLimitChecks = false;
        _upperLimit = high;
        _lowerLimit = low;
    }
}



bool StepperController::start()
{
    _running = true; //tell the stepping code we're running
    _previousMicros = micros(); //start the time to the first step (hopefully tiny)
    _stepRelativePosition = 0l; //we started stepping
    _cumulativeMicroDiffs = 0l;
    _stepsTaken = 0;
    _autoHomingRunOut = false;
    _currentSpeed = 0;
    _stepDelayTime = 0l;
    _decelerating = false;
    _lastMoveCompleteFlag = false;
    //first thing to do is check if we've hit a relay
    int limited = this->checkLimitSwitches();
    if (limited > 0)
    {
        return this->runLimitedValidations(limited);
    } else {
        digitalWrite(_enablePin, LOW);
        _enabled = true;
    }
    return true;
}

void StepperController::stop()
{
    _stepIndexMode = false;
    _autoHoming = false;
    _runningToEnd = false;
    _running = false;
    _decelerating = false;
    _lastMoveCompleteFlag = true;
    if (_moveCompleteEventFunction)
        (*_moveCompleteEventFunction)(_stepperId);
}

void StepperController::disableController(int isDisabled)
{
    if (_enablePin <= 0)
        return;
    digitalWrite(_enablePin, isDisabled); 
}
void StepperController::setDisableOnLimit(bool dis)
{
    _disableOnLimit = dis;
}
bool StepperController::isDisableOnLimit()
{
    return _disableOnLimit;
}
void StepperController::setAccel(int accel)
{
    _acceleration = accel;
}
long StepperController::getLimitUpper()
{
    return _upperLimit;
}
long StepperController::getLimitLower()
{
    return _lowerLimit;
}
long StepperController::getPosition()
{
    return _stepPosition;
}
long StepperController::stepsRelativePosition()
{
    return _stepRelativePosition;
}
long StepperController::getStepsTaken()
{
    return _stepsTaken;
}
long StepperController::getStepDelayTime()
{
    return _stepDelayTime;
}
long StepperController::getDecelStep()
{
    return _decelStep;
}
int StepperController::getMaxSpeed()
{
    return _maxSpeed;
}
int StepperController::getCurrentSpeed()
{
    return _currentSpeed;
}
bool StepperController::getIsDecel()
{
    return _decelerating;
}
float StepperController::getTestSpeed()
{
    return _testSpeed;
}
float StepperController::getDivisor()
{
    return _testDivisor;
}
float StepperController::getFloatResult()
{
    return _floatResult;
}
void StepperController::returnHome()
{
    if (_stepPosition != 0l)
        this->moveSteps(_stepPosition * -1l);
}
bool StepperController::isHomeSwitchActive()
{
    return _homeSwitchThrown;
}
bool StepperController::isEndSwitchActive()
{
    return _endSwitchThrown;
}
void StepperController::moveSteps(long steps)
{

    stepsWanted = steps;
    _stepIndexMode = true;

    long stepsToDecel = _maxSpeed / _acceleration;

    if (steps > 0) //positive steps mean moving away from home
    {
        this->setDirection(DIRECTION_FORWARD);
        //calculate step to begin deceleration
        _decelStep = (_stepPosition + stepsWanted) - stepsToDecel;
    } else {
        this->setDirection(DIRECTION_REVERSE);
        _decelStep = (_stepPosition + stepsWanted) + stepsToDecel;
    }

    this->start();
}

void StepperController::moveTo(long pos)
{
    _decelStep = -1;
    stepsWanted = pos - _stepPosition;
    this->moveSteps(stepsWanted);
}

bool StepperController::runLimitedValidations(int limited)
{
    if (_direction == DIRECTION_REVERSE && limited == STEPPER_END_LIMIT) //if we're moving toward HOME and the end switch is enabled, re-enable the motor
    {
        //enable movement via pin
        if (!_enabled)
        {
            digitalWrite(_enablePin, LOW);
            _enabled = true;
            delay(50);
        }
    } else if (_direction == DIRECTION_FORWARD && limited == STEPPER_HOME_LIMIT) //if we're moving toward END and the home switch is enabled, re-enable the motor
    {
        //enable movement via pin
        if (!_enabled)
        {
            digitalWrite(_enablePin, LOW);
            _enabled = true;
            delay(50);
        }
    } else if (limited == STEPPER_HOME_LIMIT && _autoHoming && !_autoHomingRunOut)
    {
        _autoHomingRunOut = true;
        //set home so there is a starting point
        this->setHome();
        
        this->changeDirection(); //reverse
    } else if (_disableOnLimit && _enablePin > 0)
    {
        //disable movement via pin
        digitalWrite(_enablePin, HIGH);
        _enabled = false;
        this->stop();
        return false;
    } else {
        //technically we should let it keep going but .. lets stop it manually for now
        this->stop();
        return false;
    }
    return true;
}

bool StepperController::step()
{
    //make sure we're running, otherwise we dont need to do anything
    if (!_running)
        return false;

    //first thing to do is check if we've hit a relay
    int limited = this->checkLimitSwitches();
    if (limited > 0)
    {
        if (!this->runLimitedValidations(limited))
            return false;
    } else {
        //setting home, hits home limit, runs away until it's not triggered, then set new home and stop
        if (_autoHomingRunOut && getPosition() > _limitSwitchBackoffSteps)
        {
            _lastHomeCompleteFlag = true;
            this->setHome();
            _autoHoming = false;
            _autoHomingRunOut = false;
            this->stop();
            return false;
        }
    }


    if (this->checkAutoStop())
        return false; //stop if we reach our destination

    unsigned long currentMicros = micros();

    //is it time to step?
    if ((currentMicros - _previousMicros) >= _stepDelayTime) {

        this->calcSpeed();
        _lastMicroDiff = (currentMicros - _previousMicros);
        _cumulativeMicroDiffs += _lastMicroDiff;
        long adder = 0l;
        if (_direction == DIRECTION_FORWARD)
            adder++;
        else
            adder--;

        //check if we're outside our limits
        if ((!_disableLimitChecks) && ((_stepPosition + adder > _upperLimit && !_runningToEnd) || (_stepPosition + adder < _lowerLimit && !_autoHoming)))
        {
            this->stop();
            return false;
        }

        _previousMicros = currentMicros; //update the time we last stepped
        switch (_stepPin)
        {
            case 3:
                digitalWriteFast(PIN_03, HIGH); //perform a step
                delayMicroseconds(15); //we have to hold it high for at least 5us but we do a little longer just in case
                digitalWriteFast(PIN_03, LOW);
                break;
            case 4:
                digitalWriteFast(PIN_04, HIGH); //perform a step
                delayMicroseconds(15); //we have to hold it high for at least 5us but we do a little longer just in case
                digitalWriteFast(PIN_04, LOW);
                break;
            case 5:
                digitalWriteFast(PIN_05, HIGH); //perform a step
                delayMicroseconds(15); //we have to hold it high for at least 5us but we do a little longer just in case
                digitalWriteFast(PIN_05, LOW);
                break;
            case 6:
                digitalWriteFast(PIN_06, HIGH); //perform a step
                delayMicroseconds(15); //we have to hold it high for at least 5us but we do a little longer just in case
                digitalWriteFast(PIN_06, LOW);
                break;
            case 7:
                digitalWriteFast(PIN_07, HIGH); //perform a step
                delayMicroseconds(15); //we have to hold it high for at least 5us but we do a little longer just in case
                digitalWriteFast(PIN_07, LOW);
                break;
            case 8:
                digitalWriteFast(PIN_08, HIGH); //perform a step
                delayMicroseconds(15); //we have to hold it high for at least 5us but we do a little longer just in case
                digitalWriteFast(PIN_08, LOW);
                break;
            case 9:
                digitalWriteFast(PIN_09, HIGH); //perform a step
                delayMicroseconds(15); //we have to hold it high for at least 5us but we do a little longer just in case
                digitalWriteFast(PIN_09, LOW);
                break;
            case 22:
                digitalWriteFast(PIN_22, HIGH); //perform a step
                delayMicroseconds(15); //we have to hold it high for at least 5us but we do a little longer just in case
                digitalWriteFast(PIN_22, LOW);
                break;
            case 23:
                digitalWriteFast(PIN_23, HIGH); //perform a step
                delayMicroseconds(15); //we have to hold it high for at least 5us but we do a little longer just in case
                digitalWriteFast(PIN_23, LOW);
                break;

            case 32:
                digitalWriteFast(PIN_32, HIGH); //perform a step
                delayMicroseconds(15); //we have to hold it high for at least 5us but we do a little longer just in case
                digitalWriteFast(PIN_32, LOW);
                break;
            case 33:
                digitalWriteFast(PIN_33, HIGH); //perform a step
                delayMicroseconds(15); //we have to hold it high for at least 5us but we do a little longer just in case
                digitalWriteFast(PIN_33, LOW);
                break;
        }
        _stepsTaken++;

        if (_direction == DIRECTION_FORWARD) {
            _stepRelativePosition++; //keep track of relative position
            _stepPosition++; //keep track of absolute position
        } else {
            _stepRelativePosition--; //keep track of relative position
            _stepPosition--; //keep track of absolute position
        }

        return true;
    }
    return false;
}


void StepperController::setLimitTriggerState(int state)
{
    _limitSwitchTriggerValue = state;
}

/**
 * Return if can continue
 */
int StepperController::checkLimitSwitches()
{
    
    if (_disableLimitChecks)
        return -1;

    int returnCode = 0;
    //read home pin
    int homeSwitch = _limitSwitchTriggerValue; //safe to triggered default
    switch (_homePin)
    {
        case 10:
            homeSwitch = digitalReadFast(PIN_10);
            break;
        case 14:
            homeSwitch = digitalReadFast(PIN_14);
            break;
        case 15:
            homeSwitch = digitalReadFast(PIN_15);
            break;
        case 16:
            homeSwitch = digitalReadFast(PIN_16);
            break;
        case 28:
            homeSwitch = digitalReadFast(PIN_28);
            break;
        case 29:
            homeSwitch = digitalReadFast(PIN_29);
            break;
        case 38:
            homeSwitch = digitalReadFast(PIN_38);
            break;
        case 39:
            homeSwitch = digitalReadFast(PIN_39);
            break;
        default:
            //default to triggered
            break;
    }
    int endSwitch = _limitSwitchTriggerValue; //safe to triggered default
    switch (_endPin)
    {
        case 10:
            endSwitch = digitalReadFast(PIN_10);
            break;
        case 14:
            endSwitch = digitalReadFast(PIN_14);
            break;
        case 15:
            endSwitch = digitalReadFast(PIN_15);
            break;
        case 16:
            endSwitch = digitalReadFast(PIN_16);
            break;
        case 30:
            endSwitch = digitalReadFast(PIN_30);
            break;
        case 31:
            endSwitch = digitalReadFast(PIN_31);
            break;
        case 40:
            endSwitch = digitalReadFast(PIN_40);
            break;
        case 41:
            endSwitch = digitalReadFast(PIN_41);
            break;
        default:
            //default to triggered
            break;
    }

    if (homeSwitch == _limitSwitchTriggerValue || (homeSwitch > 0 && _limitSwitchTriggerValue > 0)) //home pin is activated
    {
        if (!_homeSwitchThrown)
        {
            _homeSwitchThrown = true;
            if (_limitSwitchEventHomeFunction)
                (*_limitSwitchEventHomeFunction)(_stepperId);
        }
        returnCode = STEPPER_HOME_LIMIT;
    } else {
        
        //if neither are pressed release the thrown flag
        _homeSwitchThrown = false;
    }

    if (endSwitch == _limitSwitchTriggerValue || (endSwitch > 0 && _limitSwitchTriggerValue > 0)) //end pin is activated
    {
        if (_runningToEnd)
        {
            _runningToEnd = false;
            _upperLimit = _stepPosition; //set the upper limit to this
        }
        if (!_endSwitchThrown)
        {
            _endSwitchThrown = true;
            if (_limitSwitchEventEndFunction)
                (*_limitSwitchEventEndFunction)(_stepperId);
        }
        returnCode = STEPPER_END_LIMIT;
    } else {
        _endSwitchThrown = false;
    }

    return returnCode;

}

bool StepperController::checkAutoStop()
{
    if (_stepIndexMode && stepsWanted == _stepRelativePosition)
    {
        this->stop();
        return true;
    }
    return false;
}