unsigned long _timestampLatchActivated = 0; //when we activate latch, start a timer
int _latchDelay = 1000; //1 second to hold latch open

unsigned long _timestampActuatorActivated = 0; //when we start an actuator move, could be forward or reverse
int _actuatorMoveTime = 6500; //how long to move the actuator

unsigned long _timestampReset = 0; //Sets the time when the laser sensor trips, starts a countdown until we reset the flap

int _waitTimeForReset = 750; //how long do we wait after the flap closes to start opening again, should this be based on a score/ball return sensor reading instead?????

bool _laserEnable = false; //when the laser sensor is enabled and ready
bool _laserOn = false;
bool _flapDirectionUp = true;

void initFlap()
{
    _flapDirectionUp = true;
    pinMode(PIN_LASER_ENABLE, OUTPUT);
    pinMode(PIN_LASER_SENSOR, INPUT_PULLUP);

    pinMode(PIN_ACTUATOR_FWD, OUTPUT);
    pinMode(PIN_ACTUATOR_REV, OUTPUT);

    pinMode(PIN_LATCH, OUTPUT);

    disableLaser();

}

void enableLaser()
{
    _laserEnable = true;
    //turnOnLaser(); //disabled, turning on the laser happens when we release the ball
}

void turnOnLaser()
{
    if (!_laserEnable)
        return;
    _laserOn = true;
    digitalWrite(PIN_LASER_ENABLE, HIGH);
    delay(250); //delay the loop for quarter of a second to allow the laser to turn on this way we get no false alarms on sensor trips.
    debugString("Enable Laser");
}

void disableLaser()
{
    _laserEnable = false;
    turnOffLaser();
}
void turnOffLaser()
{
    _laserOn = false;
    digitalWrite(PIN_LASER_ENABLE, LOW);
    debugString("Disable Laser");
}

int flapUp()
{
    _timestampActuatorActivated = millis();
    digitalWrite(PIN_ACTUATOR_FWD, HIGH);
    digitalWrite(PIN_ACTUATOR_REV, LOW);
    _flapDirectionUp = true;
    debugString("Flap Up");
}

int flapDown()
{
    _timestampActuatorActivated = millis();
    digitalWrite(PIN_ACTUATOR_FWD, LOW);
    digitalWrite(PIN_ACTUATOR_REV, HIGH);
    _flapDirectionUp = false;
    debugString("Flap Down");
}

int flapStop()
{
    digitalWrite(PIN_ACTUATOR_FWD, LOW);
    digitalWrite(PIN_ACTUATOR_REV, LOW);
    debugString("Flap Stop");
}

// Run each loop to check on sensors and timers
void checkFlapStuff()
{
    //if the sensor is tripped, release the latch
    checkLaserSensor();

    if (_timestampActuatorActivated > 0 && millis() - _timestampActuatorActivated > _actuatorMoveTime)
    {
        if (_flapDirectionUp) //if we were going up, stop
        {
            flapDown(); //start moving down
        } else {
            flapStop(); //we moved again, stop
            _timestampActuatorActivated = 0;
            //turnOnLaser(); // ball release enables the laser
            sendEvent(EVENT_FLAP_SET); //send remote event we're ready to shoot again
        }
    }

    //wait some time after the laser sensor has tripped then start moving the flap back up
    if (_timestampReset > 0 && millis() - _timestampReset > _waitTimeForReset)
    {
        _timestampReset = 0; //no more waiting, but set to -1 becuase we're not ready to wait for a sensor yet
        flapUp(); //move it up
        turnOffLaser(); // turn off the sensor so it doesn't trip mid-movement and reset
    }
}

void activateLatch()
{
    _timestampLatchActivated = millis(); //start time when latch was activated
    digitalWrite(PIN_LATCH, HIGH);
    debugString("Latch activated");
}

void deactivateLatch()
{
    _timestampLatchActivated = 0;
    digitalWrite(PIN_LATCH, LOW);
    debugString("Latch deactivated");
}

void checkLaserSensor()
{
    
    //read sensor, outputs 12v when it can see the laser, 0v when it cannot
    int val = digitalReadFast(PIN_LASER_SENSOR);
    if (_laserEnable && _laserOn && val == LOW && _timestampReset == 0)
    {
        debugString("Laser tripped");
        _timestampReset = millis();
        activateLatch(); //open the latch
        sendEvent(EVENT_FLAP_TRIPPED);
    }

    //if time has elapsed to leave the latch open, close it
    if (_timestampLatchActivated > 0 && millis() - _timestampLatchActivated > _latchDelay)
    {
        deactivateLatch(); //close the latch
    }
}