#ifndef StepperController_h
#define StepperController_h



#include "Arduino.h"
class StepperController
{
  public:
    StepperController(int stepPin, int dirPin, int enablePin, int homePin, int endPin);
    unsigned long _lastMicroDiff; //time we last told the stepper to move
    unsigned long _cumulativeMicroDiffs; //time we last told the stepper to move
    long stepsWanted; //how many steps to take
    bool _direction;  //forward (away from home) - true, reverse - false, 
    void changeDirection(); //switches current _direction
    bool start(); //starts moving stepper, return true/false if it started moving
    void stop(); //stops moving stepper
    bool step(); //runs the stepper, if we stepped, return true

    void setAccel(int accel); //how fast to speed up and slow down
    void setDirection(bool dir);
    void setMaxSpeed(int speed);

    void moveSteps(long steps); //move a number of steps from current location
    void moveTo(long position); //move to an absolute position

    void autoHome(); //if home pin is set, move toward that pin
    void setHome(); //sets stepPosition to 0
    void returnHome(); //inverts stepPostion and moves that far
    void runToEnd(); //if home is set and end pin is set, run to end switch
    void unsetHome();
    
    void setLimits(long high, long low); //limiting movement of the machine; NOTE: does not scale if step mode is changed, limits need reset
    long getLimitUpper(); //limiting movement of the machine; NOTE: does not scale if step mode is changed, limits need reset
    long getLimitLower(); //limiting movement of the machine; NOTE: does not scale if step mode is changed, limits need reset

    
    float getTestSpeed();
    float getDivisor();
    float getFloatResult();
    long getDecelStep();
    int getMaxSpeed();
    int getCurrentSpeed();
    bool getIsDecel();
    long getStepDelayTime();
    long stepsRelativePosition();
    long getStepsTaken();
    long getPosition(); //current position
    bool getLastMoveCompleted();
    bool getLastHomeCompleted();

    bool isHomeSwitchActive();
    bool isEndSwitchActive();
    bool isRunning(); //returns _running
    bool isDisableOnLimit();
    bool isHomed();
    void setDisableOnLimit(bool dis);
    int checkLimitSwitches(); //check for limit switch pins
    void setLimitTriggerState(int state); //sets trigger state of limit switch
    void setEventLimitHome(void (*eventFunction));
    void setEventLimitEnd(void (*eventFunction));
    void setEventMoveComplete(void (*eventFunction));
    void disableController(int isDisabled);
    
    void setId(int id); //stepper id used by events
    int getId();

  private:
    int _stepperId;
    bool _events;
    bool _homed; //determines if we set the home position
    long _stepPosition; //current number of steps from 0
    int _stepPin;
    int _dirPin;
    int _homePin; //pin for relay to determine home position
    int _endPin; //pin for far relay to determine end of run
    int _enablePin; //ping to use for enable/disable of the stepper controller
    bool _enabled; //flag to get fast status of enable/disable pin
    long _upperLimit; //Max number of steps it's allowed to move
    long _lowerLimit; //Min number of steps it's allowed to move
    bool _stepIndexMode; //whether we're stepping a specific number of steps
    bool _running; //whether we should be stepping
    bool _autoHoming; //whether we're in auto home mode for this axis
    bool _autoHomingRunOut; //once home limit is hit, this = true until home is no longer activated, then home is set
    bool _runningToEnd; //whether we're in auto home mode for this axis
    long _stepRelativePosition; //keep track of how many times we stepped
    unsigned long _previousMicros; //time we last told the stepper to move
    bool checkAutoStop();
    void calcSpeed();
    bool runLimitedValidations(int limited); //performs checks when the value of limited > 0
    long _stepsTaken; //how many times did we step
    int _acceleration; //accel value in steps per second
    bool _decelerating; //flag to tell the stepper to decelerate
    long _decelStep; //when to start decellerating if we know our path
    long _stepDelayTime; //how long to delay a step
    int _currentSpeed; //how many steps per second we're running
    int _maxSpeed; //the max steps per second we want to achieve
    float _testSpeed;
    float _testDivisor;
    float _floatResult;
    bool _homeSwitchThrown; //if we have thrown the switch, flag to ignore processing multiple times
    bool _endSwitchThrown; //if we have thrown the switch, flag to ignore processing multiple times
    bool _disableOnLimit; //disable stepper when limit switch reached
    bool _disableLimitChecks; //if there are no limits on this stepper (rotary)
    bool _lastMoveCompleteFlag; //flag tells us when we checked to see if the last move completed
    bool _lastHomeCompleteFlag; //flag tells us when we checked to see if the last homing completed
    int _limitSwitchTriggerValue; //value HIGH/LOW when switch is in triggered state
    int _limitSwitchBackoffSteps; //Once reaching the limit, how far do we backoff when auto homing?

    void (*_limitSwitchEventHomeFunction)(int stepperId); //execute when home limit switch is triggerd
    void (*_limitSwitchEventEndFunction)(int stepperId); //execute when end limit switch is triggered
    void (*_moveCompleteEventFunction)(int stepperId); //execute when current move has completed
};

#endif