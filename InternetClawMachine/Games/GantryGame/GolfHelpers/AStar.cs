using System;
using System.Collections.Generic;

namespace InternetClawMachine.Games.GantryGame.GolfHelpers
{
    public class AStar
    {
        //these are the states available for each cell.
        public int MaxIterations = 400;

        public int GridWidth;
        public int GridHeight;
        public int GridSize;

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
            GridWidth = gridWidth;
            GridHeight = gridHeight;
            GridSize = GridHeight * GridWidth;

            //define map
            _mapArray = new List<PathMapper>(GridSize);

            var xx = 0;
            var yy = 0;
            var idx = 0;
            for (idx = 0; idx < GridSize; idx++)
            {
                xx = idx % GridWidth;
                yy = (idx - idx % GridWidth) / GridWidth;
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
            int sX;
            int sY;
            var idx = 0;
            var xx = 0;
            var yy = 0;
            var size = 1;
            PathMapper newCell;
            var die = false;
            int tmpIdx;
            for (idx = 0; idx < GridSize; idx++)
            {
                newCell = _mapArray[idx];
                if (newCell.CellType != AStarCellType.CELL_FILLED)
                { //verify the origin isnt filled
                    size = 1;
                    xx = idx % GridWidth;
                    yy = (idx - idx % GridWidth) / GridWidth;

                    while (size + xx < GridWidth + maxGap && size + yy < GridHeight + maxGap)
                    {
                        die = false;
                        sX = size;
                        for (sY = 0; sY <= size; sY++)
                        {
                            tmpIdx = xx + sX + (yy + sY) * GridWidth;
                            if (tmpIdx >= _mapArray.Count || tmpIdx < 0)
                            {
                                //out of bounds we make size full size (left or bottom edges)
                                size = maxGap;
                                die = true;
                                break;
                            }
                            newCell = _mapArray[tmpIdx];
                            if (newCell.CellType == AStarCellType.CELL_FILLED)
                            {
                                die = true;
                            }
                        }
                        if (die)
                            break;
                        sY = size;
                        for (sX = 0; sX <= size; sX++)
                        {
                            tmpIdx = xx + sX + (yy + sY) * GridWidth;

                            if (tmpIdx >= _mapArray.Count || tmpIdx < 0)
                            {
                                //out of bounds we make size full size (left or bottom edges)
                                size = maxGap;
                                die = true;
                                break;
                            }
                            newCell = _mapArray[tmpIdx];
                            if (newCell.CellType == AStarCellType.CELL_FILLED)
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
                    tmpIdx = xx + yy * GridWidth;
                    _mapArray[tmpIdx].C = size;
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

            var startIdx = startX + startY * GridWidth;
            var endIdx = endX + endY * GridWidth;
            int tmpIdx;
            for (idx = startIdx; idx <= endIdx; idx++)
            {
                if (idx < 0 || idx > _mapArray.Count) { continue; } //skip out of bounds gaps
                newCell = _mapArray[idx];
                if (newCell.CellType != AStarCellType.CELL_FILLED)
                { //verify the origin isnt filled
                    size = 1;
                    xx = idx % GridWidth;
                    yy = (idx - idx % GridWidth) / GridWidth;

                    while (size + xx < GridWidth && size + yy < GridHeight)
                    {
                        die = false;
                        sX = size;
                        for (sY = 0; sY <= size; sY++)
                        {
                            tmpIdx = xx + sX + (yy + sY) * GridWidth;
                            if (tmpIdx >= _mapArray.Count || tmpIdx < 0)
                                continue;
                            newCell = _mapArray[tmpIdx];
                            if (newCell.CellType == AStarCellType.CELL_FILLED)
                            {
                                die = true;
                            }
                        }
                        if (die)
                            break;
                        sY = size;
                        for (sX = 0; sX <= size; sX++)
                        {
                            tmpIdx = xx + sX + (yy + sY) * GridWidth;
                            if (tmpIdx >= _mapArray.Count || tmpIdx < 0)
                                continue;
                            newCell = _mapArray[tmpIdx];
                            if (newCell.CellType == AStarCellType.CELL_FILLED)
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
                    tmpIdx = xx + yy * GridWidth;
                    _mapArray[tmpIdx].C = size;
                }
            }
        }

        public List<PathMapper> Solve()
        {
            return Solve(1, MaxIterations);
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
                cellPointer = cellPointer.ParentCell;
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
                    newX = _currentCell.X + xx; //set cell to be checked
                    newY = _currentCell.Y + yy; //set cell to be checked
                    if (newX >= GridWidth) continue;
                    if (newY >= GridHeight) continue;
                    if (newX < 0) continue;
                    if (newY < 0) continue;
                    newCell = _mapArray[(int)(newX + newY * GridWidth)];

                    if (newCell != null)
                    { //make sure there is a value, could be out of bounds or something
                        if (newCell.C >= gapSize) //if gap is large enough
                        {
                            if (newCell.CellType != AStarCellType.CELL_FILLED) //and it's an empty cell
                            {
                                if (!newCell.IsClosed)
                                { //and its not in the closedlist
                                  //trace(mapArray[currentCell.x + xx][addedY]);

                                    //this is a possible destination
                                    //if diagonal then check it against squares adjacent left/right/up/down
                                    canAdd = true;

                                    //no idea what to optimize
                                    if (_currentCell.X > newX) //left of the current cell
                                    {
                                        if (_currentCell.Y < newY) //up/left diagonal
                                        {
                                            if (newCell.C < gapSize) //if the gap of the destination (newCell.c) is smaller than the object (gapSize) then we cant go there
                                            {
                                                canAdd = false;
                                            }

                                            //this is checking the spot right under where the sprite will land, moving diagonal will overlap this spot on its way to the destination, make sure it's empty otherwise it will overlap
                                            idx = (int)(newX + (newY - 1) * GridWidth);
                                            if (idx >= 0 && idx < _mapArray.Count)
                                            {
                                                tmpCheckDiag1 = _mapArray[idx];
                                                if (tmpCheckDiag1.CellType == AStarCellType.CELL_FILLED || tmpCheckDiag1.C < gapSize - 1)
                                                { //size of guy minus one because it's only traversing a single diagonal block
                                                    canAdd = false;
                                                }
                                            }

                                            //this is checking the spot to the top right of the landing to make sure there is room for it to move
                                            idx = (int)(newX + gapSize + (newY + gapSize) * GridWidth);
                                            if (idx >= 0 && idx < _mapArray.Count)
                                            {
                                                tmpCheckDiag1 = _mapArray[idx];
                                                if (tmpCheckDiag1.CellType == AStarCellType.CELL_FILLED || tmpCheckDiag1.C < gapSize - 1)
                                                { //size of guy minus one because it's only traversing a single diagonal block
                                                    canAdd = false;
                                                }
                                            }
                                        }
                                        else if (_currentCell.Y > newY)
                                        { //down/left diagonal
                                            if (newCell.C < gapSize)
                                            {
                                                canAdd = false;
                                            }

                                            idx = (int)(newX + (newY + 1) * GridWidth);
                                            if (idx >= 0 && idx < _mapArray.Count)
                                            {
                                                tmpCheckDiag1 = _mapArray[idx];
                                                if (tmpCheckDiag1.CellType == AStarCellType.CELL_FILLED || tmpCheckDiag1.C < gapSize - 1)
                                                { //size of guy minus one because it's only traversing a single diagonal block
                                                    canAdd = false;
                                                }
                                            }

                                            idx = (int)(newX + gapSize + newY * GridWidth);
                                            if (idx >= 0 && idx < _mapArray.Count)
                                            {
                                                tmpCheckDiag1 = _mapArray[idx];
                                                if (tmpCheckDiag1.CellType == AStarCellType.CELL_FILLED || tmpCheckDiag1.C < gapSize - 1)
                                                { //size of guy minus one because it's only traversing a single diagonal block
                                                    canAdd = false;
                                                }
                                            }
                                        }
                                    }
                                    else if (_currentCell.X < newX)
                                    {
                                        if (_currentCell.Y < newY) //up/right diagonal
                                        {
                                            if (newCell.C < gapSize) //diagonal gap is small means there is a block to the right of that spot
                                            {
                                                canAdd = false;
                                            }

                                            //check if the block to the left of this diagonal is filled
                                            idx = (int)(newX - 1 + (newY + gapSize - 1) * GridWidth);
                                            if (idx >= 0 && idx < _mapArray.Count)
                                            {
                                                tmpCheckDiag1 = _mapArray[idx];
                                                if (tmpCheckDiag1.CellType == AStarCellType.CELL_FILLED || tmpCheckDiag1.C < gapSize)
                                                {
                                                    canAdd = false;
                                                }
                                            }

                                            //check if the block to the end bottom is filled
                                            idx = (int)(newX + gapSize - 1 + (newY - 1) * GridWidth);
                                            if (idx >= 0 && idx < _mapArray.Count)
                                            {
                                                tmpCheckDiag1 = _mapArray[idx];
                                                if (tmpCheckDiag1.CellType == AStarCellType.CELL_FILLED || tmpCheckDiag1.C < gapSize)
                                                {
                                                    canAdd = false;
                                                }
                                            }
                                        }
                                        else if (_currentCell.Y > newY)
                                        { //down/right diagonal
                                            if (newCell.C < gapSize) //diagonal gap is too small, mean there is a block to the right
                                            {
                                                canAdd = false;
                                            }
                                            //check bottom
                                            idx = (int)(newX - 1 + (newY - gapSize + 1) * GridWidth);
                                            if (idx >= 0 && idx < _mapArray.Count)
                                            {
                                                tmpCheckDiag1 = _mapArray[idx];
                                                if (tmpCheckDiag1.CellType == AStarCellType.CELL_FILLED || tmpCheckDiag1.C < gapSize)
                                                {
                                                    canAdd = false;
                                                }
                                            }
                                            //check right block
                                            idx = (int)(newX + gapSize - 1 + (newY + 1) * GridWidth);
                                            if (idx >= 0 && idx < _mapArray.Count)
                                            {
                                                tmpCheckDiag1 = _mapArray[idx];
                                                if (tmpCheckDiag1.CellType == AStarCellType.CELL_FILLED || tmpCheckDiag1.C < gapSize)
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
                g = _currentCell.G + 1;

                h = Math.Abs(adjacentCell[ii].X - _destinationCell.X) + Math.Abs(adjacentCell[ii].Y - _destinationCell.Y);
                //h = Point.distance(new Point(adjacentCell[ii].x, adjacentCell[ii].y), new Point(destinationCell.x, destinationCell.y));

                if (!adjacentCell[ii].Visited)
                { //is cell already on the open list? - no
                    adjacentCell[ii].Visited = true;
                    adjacentCell[ii].F = g + h;
                    adjacentCell[ii].ParentCell = _currentCell;
                    adjacentCell[ii].G = g;
                    _openList.Add(adjacentCell[ii]);
                }
                else
                { //is cell already on the open list? - yes
                    if (adjacentCell[ii].G < _currentCell.ParentCell.G)
                    {
                        _currentCell.ParentCell = adjacentCell[ii];
                        _currentCell.G = adjacentCell[ii].G + 1;
                        _currentCell.F = adjacentCell[ii].G + h;
                    }
                }
            }

            //Remove current cell from openList and add to closedList.
            var indexOfCurrent = _openList.IndexOf(_currentCell);
            _closedList.Add(_currentCell);
            _currentCell.IsClosed = true;

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
            return _mapArray[xx + yy * GridWidth];
        }

        //Sets individual cell state
        public void SetCell(float x, float y, AStarCellType cellType)
        {
            _mapArray[(int)(x + y * GridWidth)].CellType = cellType;
        }

        //Toggle cell between "filled" and "free" states
        public void ToggleCell(int cellX, int cellY)
        {
            if (_mapArray[cellX + cellY * GridWidth].CellType == AStarCellType.CELL_FREE)
                _mapArray[cellX + cellY * GridWidth].CellType = AStarCellType.CELL_FILLED;
            else
                _mapArray[cellX + cellY * GridWidth].CellType = AStarCellType.CELL_FREE;
        }

        //Sets origin and destination
        public void SetPoints(float sX, float sY, float dX, float dY)
        {
            _originCell = _mapArray[(int)(sX + sY * GridWidth)];
            _destinationCell = _mapArray[(int)(dX + dY * GridWidth)];

            _originType = _originCell.CellType; //store what it used to be
            _destinationType = _destinationCell.CellType;

            _originCell.CellType = AStarCellType.CELL_ORIGIN;
            _destinationCell.CellType = AStarCellType.CELL_DESTINATION;

            _currentCell = _originCell;
            _closedList.Add(_originCell);
        }

        /**
         * reset start and destination to their original state
         */

        public void ResetPoints()
        {
            _originCell.CellType = _originType; //reset to what they were
            _destinationCell.CellType = _destinationType;
        }

        //Resets algorithm without clearing cells
        public void Reset()
        {
            for (var xx = 0; xx < GridSize; xx++)
            {
                _mapArray[xx].ParentCell = null;
                _mapArray[xx].G = 0;
                _mapArray[xx].F = 0;
                _mapArray[xx].Visited = false;
                _mapArray[xx].IsClosed = false;
            }

            _openList.Clear();
            _closedList.Clear();

            _currentCell = _originCell;
            _closedList.Add(_originCell);
        }

        //Sets all filled cells to free cells (does not affect origin or destination cells)
        public void ClearMap()
        {
            var xx = 0;
            var yy = 0;
            var idx = 0;
            for (idx = 0; idx < GridSize; idx++)
            {
                xx = idx % GridWidth;
                yy = (idx - idx % GridWidth) / GridWidth;
                if (_mapArray[idx].CellType == AStarCellType.CELL_FILLED) _mapArray[idx].CellType = AStarCellType.CELL_FREE;
                _mapArray[idx].ParentCell = null;
                _mapArray[idx].G = 0;
                _mapArray[idx].F = 0;
                _mapArray[idx].C = 0;
                _mapArray[idx].Visited = false;
                _mapArray[idx].IsClosed = false;
                _mapArray[idx].X = xx;
                _mapArray[idx].Y = yy;
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