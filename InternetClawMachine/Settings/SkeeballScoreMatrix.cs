using System.Collections.Generic;

namespace InternetClawMachine.Settings
{
    public class SkeeballScoreMatrix
    {
        public string Name { set; get; }
        public Dictionary<int, SkeeballScoreMatrixSlot> Matrix { set; get; }
    }
}