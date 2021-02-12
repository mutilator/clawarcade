using System;

namespace InternetClawMachine.Games.GantryGame.GolfHelpers
{
    public class PathMapper : IComparable<PathMapper>
    {
        public AStarCellType _cellType = 0;
        public PathMapper _parentCell = null;
        public int _g;
        public float _f;
        public int _c;
        public float _x;
        public float _y;
        public bool _isClosed = false;
        public bool _visited = false;

        public PathMapper()
        {
        }

        public PathMapper(float h, float i)
        {
            _x = h;
            _y = i;
        }

        public int CompareTo(PathMapper cell2)
        {
            return _f < cell2._f ? 1 : _f > cell2._f ? -1 : 0; //descending
        }
    }
}