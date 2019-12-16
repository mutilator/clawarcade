using System;

namespace InternetClawMachine.Games.GantryGame.GolfHelpers
{
    public class PathMapper : IComparable<PathMapper>
    {
        public AStarCellType CellType = 0;
        public PathMapper ParentCell = null;
        public int G;
        public float F;
        public int C;
        public float X;
        public float Y;
        public bool IsClosed = false;
        public bool Visited = false;

        public PathMapper()
        {
        }

        public PathMapper(float h, float i)
        {
            X = h;
            Y = i;
        }

        public int CompareTo(PathMapper cell2)
        {
            return (this.F < cell2.F) ? 1 : (this.F > cell2.F) ? -1 : 0; //descending
        }
    }
}