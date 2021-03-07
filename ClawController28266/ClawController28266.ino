
#include <WiFiManager.h>
#include <ESP8266WiFi.h>

WiFiManager wifiManager;

const int _clientCount = 4; //how many clients do we allow to connect

WiFiServer _server(23);
WiFiClient _clients[_clientCount];

void setup() {
    Serial.begin(115200);
    wifiManager.autoConnect();

    while (WiFi.status() != WL_CONNECTED) {
        Serial.print('.');
        delay(500);
    }
    
    //init server
    _server.begin();
    delay(1000);
}

void loop() {
  
  handleTelnetConnectors();
  handleSerialCommands();

}

/**
 *
 *  SERIAL COMMUNICATION
 *
 */

void handleSerialCommands()
{
    //if we have data, read it
    while (Serial.available()) //burn through data
    {
        char thisChar = Serial.read();
        broadcastToClients(thisChar);
    }
}

/**
 *
 *  NETWORK COMMUNICATION
 *
 */


void broadcastToClients(char myByte)
{
    for (byte i=0; i < _clientCount; i++)
    {
        if (_clients[i] && _clients[i].connected())
            _clients[i].write(myByte);
    }
}


void handleTelnetConnectors()
{
    // see if someone said something
    WiFiClient client = _server.available();

    //check for disconnected clients
    for (byte i=0; i < _clientCount; i++)
    {
        if (_clients[i] && !_clients[i].connected())
            _clients[i].stop();
    }

    //new client connected
    if (client) {
        bool foundSlot = false;
        for (byte i=0; i < _clientCount; i++)
        {
            if (!_clients[i] || !_clients[i].connected()) {
                //add this person to the list of clients
                _clients[i] = client;
                client.flush();
                foundSlot = true;
                break;
            }
        }
        //if there was no slot open, take the first one
        if (!foundSlot)
        {
            _clients[0].flush();
            _clients[0].stop();
            _clients[0] = client;
            client.flush();
        }
    }

    //check for new data from everyone
    for (byte i=0; i < _clientCount; i++)
    {
        //read all data in buffer
        while(_clients[i] && _clients[i].available())
        {
            //handle next byte of data
            Serial.write(_clients[i].read());
        }
    }
}