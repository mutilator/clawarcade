using System;
using System.Collections.Generic;

namespace InternetClawMachine.Games.GantryGame.GolfHelpers
{
    public class AStar
    {
        //these are the states available for each cell.
        public int _maxIterations = 400;

        public int _gridWidth;
        public int _gridHeight;
        public int _gridSize;

        private PathMapper _originCell;
        private PathMapper _destinationCell;
        private PathMapper _currentCell;

        private List<PathMapper> _openList;
        private List<PathMapper> _closedList;

        private List<PathMapper> _mapArray;

        private AStarCellType _originType;
        private AStarCellType _destinationType;

        //grid sizes refer to the size in units of cell size, not pixel size.
        public void SetStarMap(int gridWidth, int gridHeight)
        {
            _gridWidth = gridWidth;
            _gridHeight = gridHeight;
            _gridSize = _gridHeight * _gridWidth;

            //define map
            _mapArray = new List<PathMapper>(_gridSize);

            var idx = 0;
            for (idx = 0; idx < _gridSize; idx++)
            {
                var xx = idx % _gridWidth;
                var yy = (idx - idx % _gridWidth) / _gridWidth;
                _mapArray.Insert(idx, new PathMapper(xx, yy));
            }

            _openList = new List<PathMapper>();
            _closedList = new List<PathMapper>();
        }

        public void CalculateGaps()
        {
            CalculateGaps(-1);
        }

        /**
         * Calculate gaps for all tiles on map
         * @param	maxGap - Maximum size a unit will ever be, set this to speed up calculations, set to -1 to disable
         */

        public void CalculateGaps(int maxGap)
        {
            var idx = 0;
            for (idx = 0; idx < _gridSize; idx++)
            {
                var newCell = _mapArray[idx];
                if (newCell._cellType != AStarCellType.CELL_FILLED)
                { //verify the origin isnt filled
                    var size = 1;
                    var xx = idx % _gridWidth;
                    var yy = (idx - idx % _gridWidth) / _gridWidth;

                    int tmpIdx;
                    while (size + xx < _gridWidth + maxGap && size + yy < _gridHeight + maxGap)
                    {
                        var die = false;
                        var sX = size;
                        int sY;
                        for (sY = 0; sY <= size; sY++)
                        {
                            tmpIdx = xx + sX + (yy + sY) * _gridWidth;
                            if (tmpIdx >= _mapArray.Count || tmpIdx < 0)
                            {
                                //out of bounds we make size full size (left or bottom edges)
                                size = maxGap;
                                die = true;
                                break;
                            }
                            newCell = _mapArray[tmpIdx];
                            if (newCell._cellType == AStarCellType.CELL_FILLED)
                            {
                                die = true;
                            }
                        }
                        if (die)
                            break;
                        sY = size;
                        for (sX = 0; sX <= size; sX++)
                        {
                            tmpIdx = xx + sX + (yy + sY) * _gridWidth;

                            if (tmpIdx >= _mapArray.Count || tmpIdx < 0)
                            {
                                //out of bounds we make size full size (left or bottom edges)
                                size = maxGap;
                                die = true;
                                break;
                            }
                            newCell = _mapArray[tmpIdx];
                            if (newCell._cellType == AStarCellType.CELL_FILLED)
                            {
                                die = true;
                            }
                        }
                        if (die)
                            break;

                        //also stop if the size of the area is the max gap size
                        if (size == maxGap)
                            break;
                        size++;
                    }
                    tmpIdx = xx + yy * _gridWidth;
                    _mapArray[tmpIdx]._c = size;
                }
            }
        }

        public void RecalculateGaps(int eX, int eY)
        {
            RecalculateGaps(eX, eY, 1);
        }

        /**
         * Recalculate the gap around a specific tile
         * @param	eX - tile x
         * @param	eY - tile y
         * @param	maxGap - max size of an object, used to speed up gap calculator
         */

        public void RecalculateGaps(int eX, int eY, int maxGap)
        {
            PathMapper newCell;
            int sX;
            int sY;
            var idx = 0;
            var xx = 0;
            var yy = 0;
            var size = 1;
            var die = false;
            var startX = eX - maxGap;
            var startY = eY - maxGap;
            var endX = eX + maxGap;
            var endY = eY + maxGap;

            var startIdx = startX + startY * _gridWidth;
            var endIdx = endX + endY * _gridWidth;
            int tmpIdx;
            for (idx = startIdx; idx <= endIdx; idx++)
            {
                if (idx < 0 || idx > _mapArray.Count) { continue; } //skip out of bounds gaps
                newCell = _mapArray[idx];
                if (newCell._cellType != AStarCellType.CELL_FILLED)
                { //verify the origin isnt filled
                    size = 1;
                    xx = idx % _gridWidth;
                    yy = (idx - idx % _gridWidth) / _gridWidth;

                    while (size + xx < _gridWidth && size + yy < _gridHeight)
                    {
                        die = false;
                        sX = size;
                        for (sY = 0; sY <= size; sY++)
                        {
                            tmpIdx = xx + sX + (yy + sY) * _gridWidth;
                            if (tmpIdx >= _mapArray.Count || tmpIdx < 0)
                                continue;
                            newCell = _mapArray[tmpIdx];
                            if (newCell._cellType == AStarCellType.CELL_FILLED)
                            {
                                die = true;
                            }
                        }
                        if (die)
                            break;
                        sY = size;
                        for (sX = 0; sX <= size; sX++)
                        {
                            tmpIdx = xx + sX + (yy + sY) * _gridWidth;
                            if (tmpIdx >= _mapArray.Count || tmpIdx < 0)
                                continue;
                            newCell = _mapArray[tmpIdx];
                            if (newCell._cellType == AStarCellType.CELL_FILLED)
                            {
                                die = true;
                            }
                        }
                        if (die)
                            break;

                        //also stop if the size of the area is the max gap size
                        if (size == maxGap)
                            break;
                        size++;
                    }
                    tmpIdx = xx + yy * _gridWidth;
                    _mapArray[tmpIdx]._c = size;
                }
            }
        }

        public List<PathMapper> Solve()
        {
            return Solve(1, _maxIterations);
        }

        /**
         * Solve the path of an object
         * @param	gapSize - size of the object that needs pathed
         * @return Vector of waypoints for the path of the object
         */

        public List<PathMapper> Solve(int gapSize, int maxItr)
        {
            //count = 0;
            Reset();

            //trace(destinationCell.x, destinationCell.y);
            var isSolved = false;
            var iter = 0;

            isSolved = StepPathfinder(gapSize);

            while (!isSolved)
            {
                isSolved = StepPathfinder(gapSize);
                if (iter++ >= maxItr)
                {
                    //trace("too many iterations " + (iter) + ": " + (getTimer() - _timer));
                    return null;
                }
            }
            //trace(iter);

            //set pointer to last cell on list
            //if pointer is pointing to originCell, then finish
            //if pointer is not pointing at origin cell, then process, and set pointer to parent of current cell
            var solutionPath = new List<PathMapper>();
            var count = 0;
            var cellPointer = _closedList[_closedList.Count - 1];
            while (cellPointer != _originCell)
            {
                if (count++ > 2000)
                {
                    //trace("too many steps " + (iter) + ": " + (getTimer() - _timer));
                    return null; //prevent a hang in case something goes awry
                }
                solutionPath.Add(cellPointer);
                cellPointer = cellPointer._parentCell;
            }

            return solutionPath;
        }

        private bool StepPathfinder(int gapSize)
        {
            //trace(cnt++);
            if (_currentCell == _destinationCell)
            {
                _closedList.Add(_destinationCell);
                return true;
            }

            //place current cell into openList
            _openList.Add(_currentCell);

            //----------------------------------------------------------------------------------------------------
            //place all legal adjacent squares into a temporary array
            //----------------------------------------------------------------------------------------------------

            //add legal adjacent cells from above to the open list
            var adjacentCell = new List<PathMapper>();

            var canAdd = true;
            float newX;
            float newY;
            int yy;
            int xx;
            int idx;
            PathMapper newCell;
            PathMapper tmpCheckDiag1;
            //checks all cells surrounding current cell
            for (xx = -1; xx <= 1; xx++)
            {
                for (yy = -1; yy <= 1; yy++)
                { //the loop check makes sure its not checking its own location
                    if (xx == 0 && yy == 0) continue;
                    newX = _currentCell._x + xx; //set cell to be checked
                    newY = _currentCell._y + yy; //set cell to be checked
                    if (newX >= _gridWidth) continue;
                    if (newY >= _gridHeight) continue;
                    if (newX < 0) continue;
                    if (newY < 0) continue;
                    newCell = _mapArray[(int)(newX + newY * _gridWidth)];

                    if (newCell != null)
                    { //make sure there is a value, could be out of bounds or something
                        if (newCell._c >= gapSize) //if gap is large enough
                        {
                            if (newCell._cellType != AStarCellType.CELL_FILLED) //and it's an empty cell
                            {
                                if (!newCell._isClosed)
                                { //and its not in the closedlist
                                  //trace(mapArray[currentCell.x + xx][addedY]);

                                    //this is a possible destination
                                    //if diagonal then check it against squares adjacent left/right/up/down
                                    canAdd = true;

                                    //no idea what to optimize
                                    if (_currentCell._x > newX) //left of the current cell
                                    {
                                        if (_currentCell._y < newY) //up/left diagonal
                                        {
                                            if (newCell._c < gapSize) //if the gap of the destination (newCell.c) is smaller than the object (gapSize) then we cant go there
                                            {
                                                canAdd = false;
                                            }

                                            //this is checking the spot right under where the sprite will land, moving diagonal will overlap this spot on its way to the destination, make sure it's empty otherwise it will overlap
                                            idx = (int)(newX + (newY - 1) * _gridWidth);
                                            if (idx >= 0 && idx < _mapArray.Count)
                                            {
                                                tmpCheckDiag1 = _mapArray[idx];
                                                if (tmpCheckDiag1._cellType == AStarCellType.CELL_FILLED || tmpCheckDiag1._c < gapSize - 1)
                                                { //size of guy minus one because it's only traversing a single diagonal block
                                                    canAdd = false;
                                                }
                                            }

                                            //this is checking the spot to the top right of the landing to make sure there is room for it to move
                                            idx = (int)(newX + gapSize + (newY + gapSize) * _gridWidth);
                                            if (idx >= 0 && idx < _mapArray.Count)
                                            {
                                                tmpCheckDiag1 = _mapArray[idx];
                                                if (tmpCheckDiag1._cellType == AStarCellType.CELL_FILLED || tmpCheckDiag1._c < gapSize - 1)
                                                { //size of guy minus one because it's only traversing a single diagonal block
                                                    canAdd = false;
                                                }
                                            }
                                        }
                                        else if (_currentCell._y > newY)
                                        { //down/left diagonal
                                            if (newCell._c < gapSize)
                                            {
                                                canAdd = false;
                                            }

                                            idx = (int)(newX + (newY + 1) * _gridWidth);
                                            if (idx >= 0 && idx < _mapArray.Count)
                                            {
                                                tmpCheckDiag1 = _mapArray[idx];
                                                if (tmpCheckDiag1._cellType == AStarCellType.CELL_FILLED || tmpCheckDiag1._c < gapSize - 1)
                                                { //size of guy minus one because it's only traversing a single diagonal block
                                                    canAdd = false;
                                                }
                                            }

                                            idx = (int)(newX + gapSize + newY * _gridWidth);
                                            if (idx >= 0 && idx < _mapArray.Count)
                                            {
                                                tmpCheckDiag1 = _mapArray[idx];
                                                if (tmpCheckDiag1._cellType == AStarCellType.CELL_FILLED || tmpCheckDiag1._c < gapSize - 1)
                                                { //size of guy minus one because it's only traversing a single diagonal block
                                                    canAdd = false;
                                                }
                                            }
                                        }
                                    }
                                    else if (_currentCell._x < newX)
                                    {
                                        if (_currentCell._y < newY) //up/right diagonal
                                        {
                                            if (newCell._c < gapSize) //diagonal gap is small means there is a block to the right of that spot
                                            {
                                                canAdd = false;
                                            }

                                            //check if the block to the left of this diagonal is filled
                                            idx = (int)(newX - 1 + (newY + gapSize - 1) * _gridWidth);
                                            if (idx >= 0 && idx < _mapArray.Count)
                                            {
                                                tmpCheckDiag1 = _mapArray[idx];
                                                if (tmpCheckDiag1._cellType == AStarCellType.CELL_FILLED || tmpCheckDiag1._c < gapSize)
                                                {
                                                    canAdd = false;
                                                }
                                            }

                                            //check if the block to the end bottom is filled
                                            idx = (int)(newX + gapSize - 1 + (newY - 1) * _gridWidth);
                                            if (idx >= 0 && idx < _mapArray.Count)
                                            {
                                                tmpCheckDiag1 = _mapArray[idx];
                                                if (tmpCheckDiag1._cellType == AStarCellType.CELL_FILLED || tmpCheckDiag1._c < gapSize)
                                                {
                                                    canAdd = false;
                                                }
                                            }
                                        }
                                        else if (_currentCell._y > newY)
                                        { //down/right diagonal
                                            if (newCell._c < gapSize) //diagonal gap is too small, mean there is a block to the right
                                            {
                                                canAdd = false;
                                            }
                                            //check bottom
                                            idx = (int)(newX - 1 + (newY - gapSize + 1) * _gridWidth);
                                            if (idx >= 0 && idx < _mapArray.Count)
                                            {
                                                tmpCheckDiag1 = _mapArray[idx];
                                                if (tmpCheckDiag1._cellType == AStarCellType.CELL_FILLED || tmpCheckDiag1._c < gapSize)
                                                {
                                                    canAdd = false;
                                                }
                                            }
                                            //check right block
                                            idx = (int)(newX + gapSize - 1 + (newY + 1) * _gridWidth);
                                            if (idx >= 0 && idx < _mapArray.Count)
                                            {
                                                tmpCheckDiag1 = _mapArray[idx];
                                                if (tmpCheckDiag1._cellType == AStarCellType.CELL_FILLED || tmpCheckDiag1._c < gapSize)
                                                {
                                                    canAdd = false;
                                                }
                                            }
                                        }
                                    }

                                    if (canAdd)
                                        adjacentCell.Add(newCell);
                                }
                            }
                        }
                    }
                }
            }

            int g;
            float h;
            var adjLen = adjacentCell.Count;
            for (var ii = 0; ii < adjLen; ii++)
            {
                g = _currentCell._g + 1;

                h = Math.Abs(adjacentCell[ii]._x - _destinationCell._x) + Math.Abs(adjacentCell[ii]._y - _destinationCell._y);
                //h = Point.distance(new Point(adjacentCell[ii].x, adjacentCell[ii].y), new Point(destinationCell.x, destinationCell.y));

                if (!adjacentCell[ii]._visited)
                { //is cell already on the open list? - no
                    adjacentCell[ii]._visited = true;
                    adjacentCell[ii]._f = g + h;
                    adjacentCell[ii]._parentCell = _currentCell;
                    adjacentCell[ii]._g = g;
                    _openList.Add(adjacentCell[ii]);
                }
                else
                { //is cell already on the open list? - yes
                    if (adjacentCell[ii]._g < _currentCell._parentCell._g)
                    {
                        _currentCell._parentCell = adjacentCell[ii];
                        _currentCell._g = adjacentCell[ii]._g + 1;
                        _currentCell._f = adjacentCell[ii]._g + h;
                    }
                }
            }

            //Remove current cell from openList and add to closedList.
            var indexOfCurrent = _openList.IndexOf(_currentCell);
            _closedList.Add(_currentCell);
            _currentCell._isClosed = true;

            _openList.RemoveAt(indexOfCurrent);

            //Take the lowest scoring openList cell and make it the current cell.
            _openList.Sort();

            if (_openList.Count == 0) return true;

            _currentCell = _openList[_openList.Count - 1];
            _openList.Remove(_currentCell);
            return false;
        }

        public PathMapper GetCell(int xx, int yy)
        {
            return _mapArray[xx + yy * _gridWidth];
        }

        //Sets individual cell state
        public void SetCell(float x, float y, AStarCellType cellType)
        {
            _mapArray[(int)(x + y * _gridWidth)]._cellType = cellType;
        }

        //Toggle cell between "filled" and "free" states
        public void ToggleCell(int cellX, int cellY)
        {
            if (_mapArray[cellX + cellY * _gridWidth]._cellType == AStarCellType.CELL_FREE)
                _mapArray[cellX + cellY * _gridWidth]._cellType = AStarCellType.CELL_FILLED;
            else
                _mapArray[cellX + cellY * _gridWidth]._cellType = AStarCellType.CELL_FREE;
        }

        //Sets origin and destination
        public void SetPoints(float sX, float sY, float dX, float dY)
        {
            _originCell = _mapArray[(int)(sX + sY * _gridWidth)];
            _destinationCell = _mapArray[(int)(dX + dY * _gridWidth)];

            _originType = _originCell._cellType; //store what it used to be
            _destinationType = _destinationCell._cellType;

            _originCell._cellType = AStarCellType.CELL_ORIGIN;
            _destinationCell._cellType = AStarCellType.CELL_DESTINATION;

            _currentCell = _originCell;
            _closedList.Add(_originCell);
        }

        /**
         * reset start and destination to their original state
         */

        public void ResetPoints()
        {
            _originCell._cellType = _originType; //reset to what they were
            _destinationCell._cellType = _destinationType;
        }

        //Resets algorithm without clearing cells
        public void Reset()
        {
            for (var xx = 0; xx < _gridSize; xx++)
            {
                _mapArray[xx]._parentCell = null;
                _mapArray[xx]._g = 0;
                _mapArray[xx]._f = 0;
                _mapArray[xx]._visited = false;
                _mapArray[xx]._isClosed = false;
            }

            _openList.Clear();
            _closedList.Clear();

            _currentCell = _originCell;
            _closedList.Add(_originCell);
        }

        //Sets all filled cells to free cells (does not affect origin or destination cells)
        public void ClearMap()
        {
            int idx;
            for (idx = 0; idx < _gridSize; idx++)
            {
                var xx = idx % _gridWidth;
                var yy = (idx - idx % _gridWidth) / _gridWidth;
                if (_mapArray[idx]._cellType == AStarCellType.CELL_FILLED) _mapArray[idx]._cellType = AStarCellType.CELL_FREE;
                _mapArray[idx]._parentCell = null;
                _mapArray[idx]._g = 0;
                _mapArray[idx]._f = 0;
                _mapArray[idx]._c = 0;
                _mapArray[idx]._visited = false;
                _mapArray[idx]._isClosed = false;
                _mapArray[idx]._x = xx;
                _mapArray[idx]._y = yy;
            }
        }
    } //end class

    public enum AStarCellType
    {
        CELL_FREE = 0,
        CELL_FILLED = 1,
        CELL_ORIGIN = 2,
        CELL_DESTINATION = 3
    }
}