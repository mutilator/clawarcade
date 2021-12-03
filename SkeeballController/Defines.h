#define PIN_GAME_MODE                  20 //green button
#define PIN_BLUE_BUTTON                21 // blue button
#define PIN_LIGHTS                     22 // AC Relay channel 1
#define PIN_BALL_RETURN_STOP           23 // AC Relay channel 2
#define PIN_AC_3                       24 // AC Relay channel 3
#define PIN_AC_4                       25 // AC Relay channel 4

#define PIN_SCORE_SENSOR_BALL_STOP             26
#define PIN_SCORE_SENSOR_6             27
#define PIN_SCORE_SENSOR_5             28
#define PIN_SCORE_SENSOR_4             29
#define PIN_SCORE_SENSOR_3             30
#define PIN_SCORE_SENSOR_2             31
#define PIN_SCORE_SENSOR_1             32
#define PIN_SCORE_SENSOR_BALL_RTN      33

#define PIN_SCORE_ENABLE_BALL_RTN      34
#define PIN_SCORE_ENABLE_1             35
#define PIN_SCORE_ENABLE_2             36
#define PIN_SCORE_ENABLE_3             37
#define PIN_SCORE_ENABLE_4             38
#define PIN_SCORE_ENABLE_5             39
#define PIN_SCORE_ENABLE_6             40
#define PIN_SCORE_ENASBLE_BALL_STOP             41

/*

  EVENTS

*/

#define EVENT_SCORE                    100 //response when scored
#define EVENT_PONG                     101 //ping reply
#define EVENT_GAME_RESET               102 //send a reset event from the machine
#define EVENT_BALL_RELEASED            103 //ball released
#define EVENT_BALL_RETURNED            104 //ball passed ball return

#define EVENT_INFO                     900 //Event to show when we want to pass info back

#define CONTROLLER_MODE_ONLINE         0
#define CONTROLLER_MODE_LOCAL          1


#define SCORE_SLOT_1000                1
#define SCORE_SLOT_2000                2
#define SCORE_SLOT_3000                3
#define SCORE_SLOT_4000                4
#define SCORE_SLOT_10K_RIGHT           5
#define SCORE_SLOT_10K_LEFT            6
#define SCORE_SLOT_BALL_STOP           7
#define SCORE_SLOT_RETURN              8
#define SCORE_SLOT_5000                9