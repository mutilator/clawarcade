using System;
using System.Collections.Generic;

namespace InternetClawMachine.Games.GolfHelpers
{
    public class AStar
    {
        //these are the states available for each cell.
        public int MAX_ITERATIONS = 400;

        public int gridWidth;
        public int gridHeight;
        public int gridSize;

        private PathMapper originCell;
        private PathMapper destinationCell;
        private PathMapper currentCell;

        private List<PathMapper> openList;
        private List<PathMapper> closedList;

        private List<PathMapper> mapArray;

        private AStarCellType _originType;
        private AStarCellType _destinationType;

        //grid sizes refer to the size in units of cell size, not pixel size.
        public void SetStarMap(int _gridWidth, int _gridHeight)
        {
            gridWidth = _gridWidth;
            gridHeight = _gridHeight;
            gridSize = gridHeight * gridWidth;

            //define map
            mapArray = new List<PathMapper>(gridSize);

            int xx = 0;
            int yy = 0;
            int idx = 0;
            for (idx = 0; idx < gridSize; idx++)
            {
                xx = idx % gridWidth;
                yy = (idx - (idx % gridWidth)) / gridWidth;
                mapArray.Insert(idx, new PathMapper(xx, yy));
            }

            openList = new List<PathMapper>();
            closedList = new List<PathMapper>();
        }

        public void calculateGaps()
        {
            calculateGaps(-1);
        }

        /**
         * Calculate gaps for all tiles on map
         * @param	maxGap - Maximum size a unit will ever be, set this to speed up calculations, set to -1 to disable
         */

        public void calculateGaps(int maxGap)
        {
            int sX;
            int sY;
            int idx = 0;
            int xx = 0;
            int yy = 0;
            int size = 1;
            PathMapper newCell;
            bool die = false;
            int tmpIdx;
            for (idx = 0; idx < gridSize; idx++)
            {
                newCell = mapArray[idx];
                if (newCell.CellType != AStarCellType.CELL_FILLED)
                { //verify the origin isnt filled
                    size = 1;
                    xx = idx % gridWidth;
                    yy = (idx - (idx % gridWidth)) / gridWidth;

                    while (size + xx < gridWidth + maxGap && size + yy < gridHeight + maxGap)
                    {
                        die = false;
                        sX = size;
                        for (sY = 0; sY <= size; sY++)
                        {
                            tmpIdx = (xx + sX) + ((yy + sY) * gridWidth);
                            if (tmpIdx >= mapArray.Count || tmpIdx < 0)
                            {
                                //out of bounds we make size full size (left or bottom edges)
                                size = maxGap;
                                die = true;
                                break;
                            }
                            newCell = mapArray[tmpIdx];
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
                            tmpIdx = (xx + sX) + ((yy + sY) * gridWidth);

                            if (tmpIdx >= mapArray.Count || tmpIdx < 0)
                            {
                                //out of bounds we make size full size (left or bottom edges)
                                size = maxGap;
                                die = true;
                                break;
                            }
                            newCell = mapArray[tmpIdx];
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
                    tmpIdx = xx + (yy * gridWidth);
                    mapArray[tmpIdx].C = size;
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
            int idx = 0;
            int xx = 0;
            int yy = 0;
            int size = 1;
            Boolean die = false;
            int startX = eX - maxGap;
            int startY = eY - maxGap;
            int endX = eX + maxGap;
            int endY = eY + maxGap;

            int startIdx = startX + startY * gridWidth;
            int endIdx = endX + endY * gridWidth;
            int tmpIdx;
            for (idx = startIdx; idx <= endIdx; idx++)
            {
                if (idx < 0 || idx > mapArray.Count) { continue; } //skip out of bounds gaps
                newCell = mapArray[idx];
                if (newCell.CellType != AStarCellType.CELL_FILLED)
                { //verify the origin isnt filled
                    size = 1;
                    xx = idx % gridWidth;
                    yy = (idx - (idx % gridWidth)) / gridWidth;

                    while (size + xx < gridWidth && size + yy < gridHeight)
                    {
                        die = false;
                        sX = size;
                        for (sY = 0; sY <= size; sY++)
                        {
                            tmpIdx = (xx + sX) + ((yy + sY) * gridWidth);
                            if (tmpIdx >= mapArray.Count || tmpIdx < 0)
                                continue;
                            newCell = mapArray[tmpIdx];
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
                            tmpIdx = (xx + sX) + ((yy + sY) * gridWidth);
                            if (tmpIdx >= mapArray.Count || tmpIdx < 0)
                                continue;
                            newCell = mapArray[tmpIdx];
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
                    tmpIdx = xx + (yy * gridWidth);
                    mapArray[tmpIdx].C = size;
                }
            }
        }

        public List<PathMapper> Solve()
        {
            return Solve(1, MAX_ITERATIONS);
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
            Boolean isSolved = false;
            int iter = 0;

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
            List<PathMapper> solutionPath = new List<PathMapper>();
            int count = 0;
            PathMapper cellPointer = closedList[closedList.Count - 1];
            while (cellPointer != originCell)
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

        private Boolean StepPathfinder(int gapSize)
        {
            //trace(cnt++);
            if (currentCell == destinationCell)
            {
                closedList.Add(destinationCell);
                return true;
            }

            //place current cell into openList
            openList.Add(currentCell);

            //----------------------------------------------------------------------------------------------------
            //place all legal adjacent squares into a temporary array
            //----------------------------------------------------------------------------------------------------

            //add legal adjacent cells from above to the open list
            List<PathMapper> adjacentCell = new List<PathMapper>();

            Boolean canAdd = true;
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
                    newX = currentCell.X + xx; //set cell to be checked
                    newY = currentCell.Y + yy; //set cell to be checked
                    if (newX >= gridWidth) continue;
                    if (newY >= gridHeight) continue;
                    if (newX < 0) continue;
                    if (newY < 0) continue;
                    newCell = mapArray[(int)(newX + (newY * gridWidth))];

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
                                    if ((currentCell.X > newX)) //left of the current cell
                                    {
                                        if ((currentCell.Y < newY)) //up/left diagonal
                                        {
                                            if (newCell.C < gapSize) //if the gap of the destination (newCell.c) is smaller than the object (gapSize) then we cant go there
                                            {
                                                canAdd = false;
                                            }

                                            //this is checking the spot right under where the sprite will land, moving diagonal will overlap this spot on its way to the destination, make sure it's empty otherwise it will overlap
                                            idx = (int)((newX) + ((newY - 1) * gridWidth));
                                            if (idx >= 0 && idx < mapArray.Count)
                                            {
                                                tmpCheckDiag1 = mapArray[idx];
                                                if (tmpCheckDiag1.CellType == AStarCellType.CELL_FILLED || tmpCheckDiag1.C < (gapSize - 1))
                                                { //size of guy minus one because it's only traversing a single diagonal block
                                                    canAdd = false;
                                                }
                                            }

                                            //this is checking the spot to the top right of the landing to make sure there is room for it to move
                                            idx = (int)((newX + gapSize) + ((newY + gapSize) * gridWidth));
                                            if (idx >= 0 && idx < mapArray.Count)
                                            {
                                                tmpCheckDiag1 = mapArray[idx];
                                                if (tmpCheckDiag1.CellType == AStarCellType.CELL_FILLED || tmpCheckDiag1.C < (gapSize - 1))
                                                { //size of guy minus one because it's only traversing a single diagonal block
                                                    canAdd = false;
                                                }
                                            }
                                        }
                                        else if (currentCell.Y > newY)
                                        { //down/left diagonal
                                            if (newCell.C < gapSize)
                                            {
                                                canAdd = false;
                                            }

                                            idx = (int)((newX) + ((newY + 1) * gridWidth));
                                            if (idx >= 0 && idx < mapArray.Count)
                                            {
                                                tmpCheckDiag1 = mapArray[idx];
                                                if (tmpCheckDiag1.CellType == AStarCellType.CELL_FILLED || tmpCheckDiag1.C < (gapSize - 1))
                                                { //size of guy minus one because it's only traversing a single diagonal block
                                                    canAdd = false;
                                                }
                                            }

                                            idx = (int)((newX + gapSize) + ((newY) * gridWidth));
                                            if (idx >= 0 && idx < mapArray.Count)
                                            {
                                                tmpCheckDiag1 = mapArray[idx];
                                                if (tmpCheckDiag1.CellType == AStarCellType.CELL_FILLED || tmpCheckDiag1.C < (gapSize - 1))
                                                { //size of guy minus one because it's only traversing a single diagonal block
                                                    canAdd = false;
                                                }
                                            }
                                        }
                                    }
                                    else if (currentCell.X < newX)
                                    {
                                        if (currentCell.Y < newY) //up/right diagonal
                                        {
                                            if (newCell.C < gapSize) //diagonal gap is small means there is a block to the right of that spot
                                            {
                                                canAdd = false;
                                            }

                                            //check if the block to the left of this diagonal is filled
                                            idx = (int)((newX - 1) + ((newY + gapSize - 1) * gridWidth));
                                            if (idx >= 0 && idx < mapArray.Count)
                                            {
                                                tmpCheckDiag1 = mapArray[idx];
                                                if (tmpCheckDiag1.CellType == AStarCellType.CELL_FILLED || tmpCheckDiag1.C < gapSize)
                                                {
                                                    canAdd = false;
                                                }
                                            }

                                            //check if the block to the end bottom is filled
                                            idx = (int)((newX + gapSize - 1) + ((newY - 1) * gridWidth));
                                            if (idx >= 0 && idx < mapArray.Count)
                                            {
                                                tmpCheckDiag1 = mapArray[idx];
                                                if (tmpCheckDiag1.CellType == AStarCellType.CELL_FILLED || tmpCheckDiag1.C < gapSize)
                                                {
                                                    canAdd = false;
                                                }
                                            }
                                        }
                                        else if (currentCell.Y > newY)
                                        { //down/right diagonal
                                            if (newCell.C < gapSize) //diagonal gap is too small, mean there is a block to the right
                                            {
                                                canAdd = false;
                                            }
                                            //check bottom
                                            idx = (int)((newX - 1) + ((newY - gapSize + 1) * gridWidth));
                                            if (idx >= 0 && idx < mapArray.Count)
                                            {
                                                tmpCheckDiag1 = mapArray[idx];
                                                if (tmpCheckDiag1.CellType == AStarCellType.CELL_FILLED || tmpCheckDiag1.C < gapSize)
                                                {
                                                    canAdd = false;
                                                }
                                            }
                                            //check right block
                                            idx = (int)((newX + gapSize - 1) + ((newY + 1) * gridWidth));
                                            if (idx >= 0 && idx < mapArray.Count)
                                            {
                                                tmpCheckDiag1 = mapArray[idx];
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
            int adjLen = adjacentCell.Count;
            for (int ii = 0; ii < adjLen; ii++)
            {
                g = currentCell.G + 1;

                h = Math.Abs(adjacentCell[ii].X - destinationCell.X) + Math.Abs(adjacentCell[ii].Y - destinationCell.Y);
                //h = Point.distance(new Point(adjacentCell[ii].x, adjacentCell[ii].y), new Point(destinationCell.x, destinationCell.y));

                if (!adjacentCell[ii].visited)
                { //is cell already on the open list? - no
                    adjacentCell[ii].visited = true;
                    adjacentCell[ii].F = g + h;
                    adjacentCell[ii].ParentCell = currentCell;
                    adjacentCell[ii].G = g;
                    openList.Add(adjacentCell[ii]);
                }
                else
                { //is cell already on the open list? - yes
                    if (adjacentCell[ii].G < currentCell.ParentCell.G)
                    {
                        currentCell.ParentCell = adjacentCell[ii];
                        currentCell.G = adjacentCell[ii].G + 1;
                        currentCell.F = adjacentCell[ii].G + h;
                    }
                }
            }

            //Remove current cell from openList and add to closedList.
            int indexOfCurrent = openList.IndexOf(currentCell);
            closedList.Add(currentCell);
            currentCell.IsClosed = true;

            openList.RemoveAt(indexOfCurrent);

            //Take the lowest scoring openList cell and make it the current cell.
            openList.Sort();

            if (openList.Count == 0) return true;

            currentCell = openList[openList.Count - 1];
            openList.Remove(currentCell);
            return false;
        }

        public PathMapper GetCell(int xx, int yy)
        {
            return mapArray[xx + (yy * gridWidth)];
        }

        //Sets individual cell state
        public void SetCell(float x, float y, AStarCellType cellType)
        {
            mapArray[(int)(x + (y * gridWidth))].CellType = cellType;
        }

        //Toggle cell between "filled" and "free" states
        public void ToggleCell(int cellX, int cellY)
        {
            if (mapArray[cellX + (cellY * gridWidth)].CellType == AStarCellType.CELL_FREE)
                mapArray[cellX + (cellY * gridWidth)].CellType = AStarCellType.CELL_FILLED;
            else
                mapArray[cellX + (cellY * gridWidth)].CellType = AStarCellType.CELL_FREE;
        }

        //Sets origin and destination
        public void SetPoints(float sX, float sY, float dX, float dY)
        {
            originCell = mapArray[(int)(sX + (sY * gridWidth))];
            destinationCell = mapArray[(int)(dX + (dY * gridWidth))];

            _originType = originCell.CellType; //store what it used to be
            _destinationType = destinationCell.CellType;

            originCell.CellType = AStarCellType.CELL_ORIGIN;
            destinationCell.CellType = AStarCellType.CELL_DESTINATION;

            currentCell = originCell;
            closedList.Add(originCell);
        }

        /**
         * reset start and destination to their original state
         */

        public void ResetPoints()
        {
            originCell.CellType = _originType; //reset to what they were
            destinationCell.CellType = _destinationType;
        }

        //Resets algorithm without clearing cells
        public void Reset()
        {
            for (int xx = 0; xx < gridSize; xx++)
            {
                mapArray[xx].ParentCell = null;
                mapArray[xx].G = 0;
                mapArray[xx].F = 0;
                mapArray[xx].visited = false;
                mapArray[xx].IsClosed = false;
            }

            openList.Clear();
            closedList.Clear();

            currentCell = originCell;
            closedList.Add(originCell);
        }

        //Sets all filled cells to free cells (does not affect origin or destination cells)
        public void clearMap()
        {
            int xx = 0;
            int yy = 0;
            int idx = 0;
            for (idx = 0; idx < gridSize; idx++)
            {
                xx = idx % gridWidth;
                yy = (idx - (idx % gridWidth)) / gridWidth;
                if (mapArray[idx].CellType == AStarCellType.CELL_FILLED) mapArray[idx].CellType = AStarCellType.CELL_FREE;
                mapArray[idx].ParentCell = null;
                mapArray[idx].G = 0;
                mapArray[idx].F = 0;
                mapArray[idx].C = 0;
                mapArray[idx].visited = false;
                mapArray[idx].IsClosed = false;
                mapArray[idx].X = xx;
                mapArray[idx].Y = yy;
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