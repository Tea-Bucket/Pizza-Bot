using PizzaBot.Models;
using Pomelo.EntityFrameworkCore.MySql.Query.Internal;

namespace PizzaBot.Services
{
    public class PizzaBalancingService
    {

        private readonly GlobalStuffService _globalStuffService;
        private PizzaConfig _config;
        public PizzaBalancingService(GlobalStuffService globalStuffService)
        {
            _globalStuffService = globalStuffService;
        }

        struct DistributionPerformance
        {
            public float maxPenalty;
            public float avgPenalty;
            public float penaltyVariance;
            public float penaltyStandardDeviation;
        }

        public (float penalty, bool isOk) CalculatePenalty(PizzaRequest request, PizzaResult result)
        {
            int reqMeat = request.reqPiecesMeat;
            int reqVeggie = request.reqPiecesVegetarian;
            int reqVegan = request.reqPiecesVegan;

            int resMeat = result.resPiecesMeat;
            int resVeggie = result.resPiecesVegetarian;
            int resVegan = result.resPiecesVegan;

            float categoryToleranceMeatVeggie = request.priority;

            int reqNum = reqMeat + reqVeggie + reqVegan;
            int resNum = resMeat + resVeggie + resVegan;

            uint diffNumReqRes = (uint)Math.Abs(reqNum - resNum);

            float absoluteMeatShare = Math.Abs(reqMeat - resMeat);
            float absoluteVeggieShare = Math.Abs(reqVeggie - resVeggie);
            float absoluteVeganShare = Math.Abs(reqVegan - resVegan);

            float penalty = 1000;

            //use either modified tuxic penalty or pfeiffer-treimerpenalty
            if (_config.PenaltyType == PenaltyType.Tuxic)
            {
                float epsilon = 0.001f;

                float percentMeatInRequest = reqMeat / (reqNum + epsilon); // 0 - 1
                float percentMeatInResult = resMeat / (resNum + epsilon); // 0 - 1

                float percentVeggiInRequest = reqVeggie / (reqNum + epsilon);
                float percentVeggiInResult = resVeggie / (resNum + epsilon);

                float percentVeganInRequest = reqVegan / (reqNum + epsilon);
                float percentVeganInResult = resVegan / (resNum + epsilon);

                float diffMeatShareResReq = Math.Abs(percentMeatInResult - percentMeatInRequest); // 0 - 1
                float diffVeggiShareResReq = Math.Abs(percentVeggiInResult - percentVeggiInRequest);
                float diffVeganShareResReq = Math.Abs(percentVeganInResult - percentVeganInRequest);
                //result is in 0.01 to sq(diffNum)*catTol*0.99
                float num_base = 0.01f;
                float penaltyCount = (diffNumReqRes * diffNumReqRes) * (categoryToleranceMeatVeggie * (1 - num_base) + num_base);

                float penaltyCat = (diffMeatShareResReq * diffMeatShareResReq + diffVeggiShareResReq * diffVeggiShareResReq + diffVeganShareResReq * diffVeganShareResReq) 
                    / (3 * categoryToleranceMeatVeggie + epsilon);

                float f = 0.5f; // fav: 0.0: num, 1.0: cat
                penalty = ((1.0f - f) * penaltyCount + f * penaltyCat) / reqNum;
            }
            else if (_config.PenaltyType == PenaltyType.PfeifferTreimer)
            {
                float penaltyCount = diffNumReqRes;
                float penaltyCat = (absoluteMeatShare  + absoluteVeggieShare + absoluteVeganShare) / 3;
                penalty = (categoryToleranceMeatVeggie * penaltyCount + (1.0f - categoryToleranceMeatVeggie) * penaltyCat);
            }else if (_config.PenaltyType == PenaltyType.PfeifferTreimerLockedDown)
            {
                // float divider = (_config.Fragments - 1 + epsilon); So uhm, it turned out, after testing, the divider made a difference, but it made it worse, like very very slightly
                // float penaltyCount = diffNumReqRes / divider;
                // float penaltyCat = (absoluteMeatShare  + absoluteVeggieShare + absoluteVeganShare) / (divider * 3);
                float penaltyCount = diffNumReqRes;

                float penaltyCat = (absoluteMeatShare  + absoluteVeggieShare + absoluteVeganShare) / 3;


                // take weighted average of both penalties using categoryToleranceMeatVeggie
                float f = categoryToleranceMeatVeggie; // fav: 1.0: num, 0.0: cat
                f = f * 0.7f + 0.2f;
                penalty = (f * penaltyCount + (1.0f - f) * penaltyCat);
            }
            else
            {
                throw new Exception("Unknown penalty type!");
            }

            //float f = 0.5f; // fav: 0.0: num, 1.0: cat
            //float penaltyMeatVeggi = ((1.0f - f) * penaltyCount + f * penaltyCat) / reqNum;

            //only ok, if at least one piece of each requested type has been assigned
            bool meatOk = !(reqMeat == 0 && resMeat != 0);
            bool veggieOk = !(reqVeggie == 0 && resVeggie != 0);
            bool veganOk = !(reqVegan == 0 && resVegan != 0);

            return (penalty, categoryToleranceMeatVeggie != 0.0f || (meatOk && veggieOk && veganOk));
        }

        public (Dictionary<int, PizzaResult> results, int requiredMeat, int requiredVeggie, int requiredVegan, float totalCost) Distribute(Dictionary<int, PizzaRequest> orders)
        {
            _config = _globalStuffService.GetConfig();

            if (_config!.PenaltyType is PenaltyType.Compact) {
                var (results, config) = CompactBalance.Distribute(orders, (uint)_config.Fragments);
                return (results, (int)config.meat, (int)config.vegetarian, (int)config.vegan, config.Reduce(0u, (acc, val) => acc + val) * _config.Price / 100.0f);
            }


            // In general, cannot determine the leveling strategy at this point.
            // However, the optimal solution follows one of four strategies:
            //   - drain both
            //   - drain V, fill P
            //   - fill P, drain V
            //   - fill both
            // So, try all these to find the optimal solution.

            // ppp should be the same for all pizzas of all categories (or?)
            // however, the user or this function could try getting better-fitting results
            // by testing other ppp values.
            // this would probably need floating point requests or distribution factors

            bool compareResults(float oldMaxPenalty, float oldAveragePenalty, float newMaxPenalty, float newAveragePenalty)
            {
                float f = 0.1f;

                float oldVal = (1.0f - f) * oldMaxPenalty + oldAveragePenalty;
                float newVal = (1.0f - f) * newMaxPenalty + newAveragePenalty;

                return newVal < oldVal;
            }

            var balanced = Balance(orders, _config.Fragments, 0);
            for (byte fillTypes = 0b0000_0001; fillTypes <= 0b0000_0111; fillTypes++)
            {
                var newBalanced = Balance(orders, _config.Fragments, fillTypes);
                if (compareResults(balanced.maxPen, balanced.avgPen, newBalanced.maxPen, newBalanced.avgPen))
                {
                    Console.WriteLine("Found better result!" + fillTypes);
                    balanced = newBalanced;
                }
            }

            float p = 1.0f / balanced.balanced.Count;
            DistributionPerformance performance = new DistributionPerformance();
            performance.maxPenalty = balanced.maxPen;
            performance.avgPenalty = balanced.avgPen;
            foreach (var result in balanced.balanced)
            {
                float d = performance.avgPenalty - result.Value.penaltyMeatVeggi;
                performance.penaltyVariance += d * d * p;
            }
            performance.penaltyStandardDeviation = (float)Math.Sqrt(performance.penaltyVariance);

            // set order price, count and verify required fragments
            int requiredMeat = 0, requiredVeggie = 0, requiredVegan = 0;
            foreach (var result in balanced.balanced)
            {
                p = (result.Value.resPiecesMeat + result.Value.resPiecesVegetarian + result.Value.resPiecesVegan) * _config.Price;
                p = (p + _config.Fragments - 1) / _config.Fragments;
                result.Value.totalCost = (float)Math.Floor(p) * 0.01f;
                requiredMeat += result.Value.resPiecesMeat;
                requiredVeggie += result.Value.resPiecesVegetarian;
                requiredVegan += result.Value.resPiecesVegan;
            }
            if (requiredMeat % _config.Fragments != 0)
            {
                throw new Exception("Meat fragments don't fit!");
            }
            if (requiredVeggie % _config.Fragments != 0)
            {
                throw new Exception("Veggie fragments don't fit!");
            }
            if (requiredVegan % _config.Fragments != 0)
            {
                throw new Exception("Vegan fragments don't fit!");
            }

            requiredMeat /= _config.Fragments;
            requiredVeggie /= _config.Fragments;
            requiredVegan /= _config.Fragments;

            float totalCost = (requiredMeat + requiredVeggie + requiredVegan) * _config.Price / 100.0f;

            return (balanced.balanced, requiredMeat, requiredVeggie, requiredVegan, totalCost);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="requests"></param>
        /// <param name="piecesPerPizza"></param>
        /// <param name="fillTypes">first byte for meat, second for veggie, third for vegan</param>
        /// <returns></returns>
        public (Dictionary<int, PizzaResult> balanced, float maxPen, float avgPen) Balance(Dictionary<int, PizzaRequest> requests, int piecesPerPizza, byte fillTypes)
        {
            Dictionary<int, PizzaResult> balanced = new Dictionary<int, PizzaResult>();

            bool fillMeat = (fillTypes & 0b0000_0001) > 0;
            bool fillVeggie = (fillTypes & 0b0000_0010) > 0;
            bool fillVegan = (fillTypes & 0b0000_0100) > 0;

            //allocated pieces per category
            int numMeat = 0, numVeggie = 0, numVegan = 0;

            //fill categories with preferences
            foreach (var request in requests)
            {
                PizzaResult result = new PizzaResult();
                result.Id = request.Value.Id;
                result.resPiecesMeat = request.Value.reqPiecesMeat;
                result.resPiecesVegetarian = request.Value.reqPiecesVegetarian;
                result.resPiecesVegan = request.Value.reqPiecesVegan;

                numMeat += request.Value.reqPiecesMeat;
                numVeggie += request.Value.reqPiecesVegetarian;
                numVegan += request.Value.reqPiecesVegan;

                balanced.Add(request.Value.Id, result);
            }


            //determine next viable pizza order with balancing strategy
            int requiredMeatPizzas = numMeat / piecesPerPizza;
            int requiredVeggiePizzas = numVeggie / piecesPerPizza;
            int requiredVeganPizzas = numVegan / piecesPerPizza;

            if (fillMeat)
            {
                requiredMeatPizzas++;
            }
            if (fillVeggie)
            {
                requiredVeggiePizzas++;
            }
            if (fillVegan)
            {
                requiredVeganPizzas++;
            }

            //determine delta of pieces to next viable pizza order
            int deltaMeat = numMeat - requiredMeatPizzas * piecesPerPizza;
            int deltaVeggie = numVeggie - requiredVeggiePizzas * piecesPerPizza;
            int deltaVegan = numVegan - requiredVeganPizzas * piecesPerPizza;

            //-----tuxic-----
            // balance using provided strategy
            // this can be done greedily, advancing to a balanced state,
            // if the advancement per operation is constant.
            // there are 2 basic operations:
            //   - compress/expand P/V (advance: 1)
            //   - move right/left (P->V / V->P) (advance: 0 or 2)
            // since we want to minimize the maximum penalty any (not every) one has to pay,
            // the cheapest/best operation is the operation that results in the lowest penalty
            // when draining both:
            //   loop (best) compress until one category fits (advance: 1)
            //   loop select (best) of (advance: 1):
            //     - (best) compress of overfull category
            //     - (best) compress of fitting category and (best) move from overfull to fitting
            // when filling both:
            //   loop (best) expand until one category fits (advance: 1)
            //   loop select (best) of (advance: 1):
            //     - (best) expand of underfull category
            //     - (best) expand of fitting category and (best) move from fitting to underfull
            // when draining D and filling F:
            //   // level operations
            //   loop select (best) of until one fits (advance: 2, but max. one on each side):
            //     - [A](best) compress D and (best) expand F
            //     - [B](best) move from D to F
            //   // drain/fill other
            //   either if (D fits):
            //     - loop select (best) of (advance: 1):
            //       - [C](best) expand F
            //       - [D](best) move from D to F and (best) expand D
            //   or if (F fits):
            //     - loop select (best) of (advance: 1):
            //       - [E](best) compress D
            //       - [F](best) move from D to F and (best) compress F
            // since [A] = [C] + [E] and [B] = [C] + [F] = [D] + [E] and [B] (^=, but simpler than) [D] + [F],
            // and so [A] and [B] are preferable to [C], [D], [E], [F],
            // this should yield the optimal solution.
            //-----tuxic-----

            if (!fillMeat && !fillVeggie && !fillVegan)
            {
                while (!(deltaMeat == 0 || deltaVeggie == 0 || deltaVegan == 0))
                {
                    // (best) compress any category
                    var scale = Scale(requests, ref balanced, -1, -1, -1);
                    deltaMeat += scale.deltaMeat;
                    deltaVeggie += scale.deltaVeggie;
                    deltaVegan += scale.deltaVegan;

                    if (scale.deltaMeat == 0 && scale.deltaVeggie == 0 && scale.deltaVegan == 0)
                    {
                        break;
                    }
                }
                while (!(deltaMeat == 0 && deltaVeggie == 0 && deltaVegan == 0))
                {
                    // (best) compress !fitting, allow deferred
                    var dupe = DuplicatePizzaResultDict(balanced);
                    var scale = Scale(requests, ref dupe, -Math.Sign(deltaMeat), -Math.Sign(deltaVeggie), -Math.Sign(deltaVegan));

                    List<byte> shiftInstr = new List<byte>();
                    if (deltaMeat == 0)
                    {
                        if (deltaVeggie != 0)
                        {
                            shiftInstr.Add(0b0010_0001);
                        }
                        if (deltaVegan != 0)
                        {
                            shiftInstr.Add(0b0100_0001);
                        }
                    }
                    if (deltaVeggie == 0)
                    {
                        if (deltaMeat != 0)
                        {
                            shiftInstr.Add(0b0001_0010);
                        }
                        if (deltaVegan != 0)
                        {
                            shiftInstr.Add(0b0100_0010);
                        }
                    }
                    if (deltaVegan == 0)
                    {
                        if (deltaMeat != 0)
                        {
                            shiftInstr.Add(0b0001_0100);
                        }
                        if (deltaVeggie != 0)
                        {
                            shiftInstr.Add(0b0010_0100);
                        }
                    }

                    var deferred = DeferredScale(requests, ref balanced, scale.bestPenalty, false, shiftInstr);

                    if (!deferred.foundBetter)
                    {
                        deltaMeat += scale.deltaMeat;
                        deltaVeggie += scale.deltaVeggie;
                        deltaVegan += scale.deltaVegan;

                        balanced = dupe;
                    }
                    else
                    {
                        deltaMeat += deferred.deltaMeat;
                        deltaVeggie += deferred.deltaVeggie;
                        deltaVegan += deferred.deltaVegan;
                    }

                    if ((scale.deltaMeat == 0 && scale.deltaVeggie == 0 && scale.deltaVegan == 0) && (deferred.deltaMeat == 0 && deferred.deltaVeggie == 0 && deferred.deltaVegan == 0))
                    {
                        break;
                    }
                }
            }
            else if (fillMeat && fillVeggie && fillVegan)
            {
                while (!(deltaMeat == 0 || deltaVeggie == 0 || deltaVegan == 0))
                {
                    // (best) expand any category
                    var scale = Scale(requests, ref balanced, 1, 1, 1);
                    deltaMeat += scale.deltaMeat;
                    deltaVeggie += scale.deltaVeggie;
                    deltaVegan += scale.deltaVegan;

                    if (scale.deltaMeat == 0 && scale.deltaVeggie == 0 && scale.deltaVegan == 0)
                    {
                        break;
                    }
                }
                while (!(deltaMeat == 0 && deltaVeggie == 0 && deltaVegan == 0))
                {
                    // (best)  expand !fitting, allow deferred
                    var dupe = DuplicatePizzaResultDict(balanced);
                    var scale = Scale(requests, ref dupe, -Math.Sign(deltaMeat), -Math.Sign(deltaVeggie), -Math.Sign(deltaVegan));

                    List<byte> shiftInstr = new List<byte>();
                    if (deltaMeat == 0)
                    {
                        if (deltaVeggie != 0)
                        {
                            shiftInstr.Add(0b0001_0010);
                        }
                        if (deltaVegan != 0)
                        {
                            shiftInstr.Add(0b0001_0100);
                        }
                    }
                    if (deltaVeggie == 0)
                    {
                        if (deltaMeat != 0)
                        {
                            shiftInstr.Add(0b0010_0001);
                        }
                        if (deltaVegan != 0)
                        {
                            shiftInstr.Add(0b0010_0100);
                        }
                    }
                    if (deltaVegan == 0)
                    {
                        if (deltaMeat != 0)
                        {
                            shiftInstr.Add(0b0100_0001);
                        }
                        if (deltaVeggie != 0)
                        {
                            shiftInstr.Add(0b0100_0010);
                        }
                    }

                    var deferred = DeferredScale(requests, ref balanced, scale.bestPenalty, true, shiftInstr);

                    if (!deferred.foundBetter)
                    {
                        deltaMeat += scale.deltaMeat;
                        deltaVeggie += scale.deltaVeggie;
                        deltaVegan += scale.deltaVegan;

                        balanced = dupe;
                    }
                    else
                    {
                        deltaMeat += deferred.deltaMeat;
                        deltaVeggie += deferred.deltaVeggie;
                        deltaVegan += deferred.deltaVegan;
                    }

                    if ((scale.deltaMeat == 0 && scale.deltaVeggie == 0 && scale.deltaVegan == 0) && (deferred.deltaMeat == 0 && deferred.deltaVeggie == 0 && deferred.deltaVegan == 0))
                    {
                        break;
                    }
                }
            }
            else
            {
                while (!(deltaMeat == 0 || deltaVeggie == 0 || deltaVegan == 0))
                {
                    var shift = HandleDiffFillDrainThree(requests, ref balanced, -Math.Sign(deltaMeat), -Math.Sign(deltaVeggie), -Math.Sign(deltaVegan));
                    if (shift.bestPenalty == float.MaxValue)
                    {
                        return (null, shift.bestPenalty, shift.bestPenalty);
                    }

                    deltaMeat += shift.deltaMeat;
                    deltaVeggie += shift.deltaVeggie;
                    deltaVegan += shift.deltaVegan;

                    if (shift.deltaMeat == 0 && shift.deltaVeggie == 0 && shift.deltaVegan == 0)
                    {
                        break;
                    }
                }
                if ((Math.Sign(deltaMeat) + Math.Sign(deltaVeggie) + Math.Sign(deltaVegan)) == -2)
                {
                    while (!(deltaMeat == 0 && deltaVeggie == 0 && deltaVegan == 0))
                    {
                        // (best)  expand !fitting, allow deferred
                        var dupe = DuplicatePizzaResultDict(balanced);
                        var scale = Scale(requests, ref dupe, -Math.Sign(deltaMeat), -Math.Sign(deltaVeggie), -Math.Sign(deltaVegan));

                        List<byte> shiftInstr = new List<byte>();
                        if (deltaMeat == 0)
                        {
                            if (deltaVeggie != 0)
                            {
                                shiftInstr.Add(0b0001_0010);
                            }
                            if (deltaVegan != 0)
                            {
                                shiftInstr.Add(0b0001_0100);
                            }
                        }
                        if (deltaVeggie == 0)
                        {
                            if (deltaMeat != 0)
                            {
                                shiftInstr.Add(0b0010_0001);
                            }
                            if (deltaVegan != 0)
                            {
                                shiftInstr.Add(0b0010_0100);
                            }
                        }
                        if (deltaVegan == 0)
                        {
                            if (deltaMeat != 0)
                            {
                                shiftInstr.Add(0b0100_0001);
                            }
                            if (deltaVeggie != 0)
                            {
                                shiftInstr.Add(0b0100_0010);
                            }
                        }

                        var deferred = DeferredScale(requests, ref balanced, scale.bestPenalty, true, shiftInstr);

                        if (!deferred.foundBetter)
                        {
                            deltaMeat += scale.deltaMeat;
                            deltaVeggie += scale.deltaVeggie;
                            deltaVegan += scale.deltaVegan;

                            balanced = dupe;
                        }
                        else
                        {
                            deltaMeat += deferred.deltaMeat;
                            deltaVeggie += deferred.deltaVeggie;
                            deltaVegan += deferred.deltaVegan;
                        }

                        if ((scale.deltaMeat == 0 && scale.deltaVeggie == 0 && scale.deltaVegan == 0) && (deferred.deltaMeat == 0 && deferred.deltaVeggie == 0 && deferred.deltaVegan == 0))
                        {
                            break;
                        }
                    }
                }
                else if ((Math.Sign(deltaMeat) + Math.Sign(deltaVeggie) + Math.Sign(deltaVegan)) == 2)
                {
                    while (!(deltaMeat == 0 && deltaVeggie == 0 && deltaVegan == 0))
                    {
                        // (best) compress !fitting, allow deferred
                        var dupe = DuplicatePizzaResultDict(balanced);
                        var scale = Scale(requests, ref dupe, -Math.Sign(deltaMeat), -Math.Sign(deltaVeggie), -Math.Sign(deltaVegan));

                        List<byte> shiftInstr = new List<byte>();
                        if (deltaMeat == 0)
                        {
                            if (deltaVeggie != 0)
                            {
                                shiftInstr.Add(0b0010_0001);
                            }
                            if (deltaVegan != 0)
                            {
                                shiftInstr.Add(0b0100_0001);
                            }
                        }
                        if (deltaVeggie == 0)
                        {
                            if (deltaMeat != 0)
                            {
                                shiftInstr.Add(0b0001_0010);
                            }
                            if (deltaVegan != 0)
                            {
                                shiftInstr.Add(0b0100_0010);
                            }
                        }
                        if (deltaVegan == 0)
                        {
                            if (deltaMeat != 0)
                            {
                                shiftInstr.Add(0b0001_0100);
                            }
                            if (deltaVeggie != 0)
                            {
                                shiftInstr.Add(0b0010_0100);
                            }
                        }

                        var deferred = DeferredScale(requests, ref balanced, scale.bestPenalty, false, shiftInstr);

                        if (!deferred.foundBetter)
                        {
                            deltaMeat += scale.deltaMeat;
                            deltaVeggie += scale.deltaVeggie;
                            deltaVegan += scale.deltaVegan;

                            balanced = dupe;
                        }
                        else
                        {
                            deltaMeat += deferred.deltaMeat;
                            deltaVeggie += deferred.deltaVeggie;
                            deltaVegan += deferred.deltaVegan;
                        }

                        if ((scale.deltaMeat == 0 && scale.deltaVeggie == 0 && scale.deltaVegan == 0) && (deferred.deltaMeat == 0 && deferred.deltaVeggie == 0 && deferred.deltaVegan == 0))
                        {
                            break;
                        }
                    }
                }
                else
                {
                    while((Math.Sign(deltaMeat) + Math.Sign(deltaVeggie) + Math.Sign(deltaVegan)) == 0)
                    {
                        var result = HandleDiffFillDrainTwo(requests, ref balanced, -Math.Sign(deltaMeat), -Math.Sign(deltaVeggie), -Math.Sign(deltaVegan));

                        deltaMeat += result.deltaMeat;
                        deltaVeggie += result.deltaVeggie;
                        deltaVegan += result.deltaVegan;

                        if (result.deltaMeat == 0 && result.deltaVeggie == 0 && result.deltaVegan == 0)
                        {
                            break;
                        }
                    }
                    while(!(deltaMeat == 0 && deltaVeggie == 0 && deltaVegan == 0))
                    {
                        var result = HandleDiffFillDrainOne(requests, ref balanced, -Math.Sign(deltaMeat), -Math.Sign(deltaVeggie), -Math.Sign(deltaVegan));

                        deltaMeat += result.deltaMeat;
                        deltaVeggie += result.deltaVeggie;
                        deltaVegan += result.deltaVegan;

                        if (result.deltaMeat == 0 && result.deltaVeggie == 0 && result.deltaVegan == 0)
                        {
                            break;
                        }
                    }

                    /*
                    if (deltaVegan == 0)
                    {
                        while (!(deltaMeat == 0 || deltaVeggie == 0))
                        {
                            byte shiftInst = (byte)(fillVeggie ? 0b0001_0010 : 0b0010_0001);
                            var shift = Shift(requests, ref balanced, shiftInst, true);
                            if (shift.bestPenalty == float.MaxValue)
                            {
                                return (null, shift.bestPenalty, shift.bestPenalty);
                            }

                            deltaMeat += shift.deltaMeat;
                            deltaVeggie += shift.deltaVeggie;
                            deltaVegan += shift.deltaVegan;

                            if (shift.deltaMeat == 0 && shift.deltaVeggie == 0 && shift.deltaVegan == 0)
                            {
                                break;
                            }
                        }
                        while (!(deltaMeat == 0 && deltaVeggie == 0))
                        {
                            var dupe = DuplicatePizzaResultDict(balanced);
                            var scale = Scale(requests, ref dupe, -Math.Sign(deltaMeat), -Math.Sign(deltaVeggie), -Math.Sign(deltaVegan));

                            List<byte> shiftInstr = new List<byte>();
                            bool shouldExpand = false;
                            if (deltaVeggie > deltaMeat)
                            {
                                shiftInstr.Add(0b010_0100);
                                if (deltaMeat == 0)
                                {
                                    shouldExpand = true;
                                }
                            }
                            if (deltaVeggie < deltaMeat)
                            {
                                shiftInstr.Add(0b00100_0010);
                                if (deltaMeat == 0)
                                {
                                    shouldExpand = true;
                                }
                            }

                            var deferred = DeferredScale(requests, ref balanced, scale.bestPenalty, shouldExpand, shiftInstr);

                            if (!deferred.foundBetter)
                            {
                                deltaMeat += scale.deltaMeat;
                                deltaVeggie += scale.deltaVeggie;
                                deltaVegan += scale.deltaVegan;

                                balanced = dupe;
                            }
                            else
                            {
                                deltaMeat += deferred.deltaMeat;
                                deltaVeggie += deferred.deltaVeggie;
                                deltaVegan += deferred.deltaVegan;
                            }

                            if ((scale.deltaMeat == 0 && scale.deltaVeggie == 0 && scale.deltaVegan == 0) && (deferred.deltaMeat == 0 && deferred.deltaVeggie == 0 && deferred.deltaVegan == 0))
                            {
                                break;
                            }
                        }
                    }
                    else if (deltaMeat == 0)
                    {
                        while (!(deltaVeggie == 0 || deltaVegan == 0))
                        {
                            byte shiftInst = (byte)(fillVegan ? 0b0010_0100 : 0b0100_0010);
                            var shift = Shift(requests, ref balanced, shiftInst, true);
                            if (shift.bestPenalty == float.MaxValue)
                            {
                                return (null, shift.bestPenalty, shift.bestPenalty);
                            }

                            deltaMeat += shift.deltaMeat;
                            deltaVeggie += shift.deltaVeggie;
                            deltaVegan += shift.deltaVegan;

                            if (shift.deltaMeat == 0 && shift.deltaVeggie == 0 && shift.deltaVegan == 0)
                            {
                                break;
                            }
                        }
                        while (!(deltaVeggie == 0 && deltaVegan == 0))
                        {
                            var dupe = DuplicatePizzaResultDict(balanced);
                            var scale = Scale(requests, ref dupe, -Math.Sign(deltaMeat), -Math.Sign(deltaVeggie), -Math.Sign(deltaVegan));

                            List<byte> shiftInstr = new List<byte>();
                            bool shouldExpand = false;
                            if (deltaVeggie > deltaVegan)
                            {
                                shiftInstr.Add(0b010_0100);
                                if (deltaVeggie == 0)
                                {
                                    shouldExpand = true;
                                }
                            }
                            if (deltaVeggie < deltaVegan)
                            {
                                shiftInstr.Add(0b00100_0010);
                                if (deltaVegan == 0)
                                {
                                    shouldExpand = true;
                                }
                            }

                            var deferred = DeferredScale(requests, ref balanced, scale.bestPenalty, shouldExpand, shiftInstr);

                            if (!deferred.foundBetter)
                            {
                                deltaMeat += scale.deltaMeat;
                                deltaVeggie += scale.deltaVeggie;
                                deltaVegan += scale.deltaVegan;

                                balanced = dupe;
                            }
                            else
                            {
                                deltaMeat += deferred.deltaMeat;
                                deltaVeggie += deferred.deltaVeggie;
                                deltaVegan += deferred.deltaVegan;
                            }

                            if ((scale.deltaMeat == 0 && scale.deltaVeggie == 0 && scale.deltaVegan == 0) && (deferred.deltaMeat == 0 && deferred.deltaVeggie == 0 && deferred.deltaVegan == 0)) ;
                        }
                    }
                    else
                    {
                        while (!(deltaMeat == 0 || deltaVegan == 0))
                        {
                            byte shiftInst = (byte)(fillVegan ? 0b0001_0100 : 0b0100_0001);
                            var shift = Shift(requests, ref balanced, shiftInst, true);
                            if (shift.bestPenalty == float.MaxValue)
                            {
                                return (null, shift.bestPenalty, shift.bestPenalty);
                            }

                            deltaMeat += shift.deltaMeat;
                            deltaVeggie += shift.deltaVeggie;
                            deltaVegan += shift.deltaVegan;

                            if (shift.deltaMeat == 0 && shift.deltaVeggie == 0 && shift.deltaVegan == 0)
                            {
                                break;
                            }
                        }
                        while (!(deltaMeat == 0 && deltaVegan == 0))
                        {
                            var dupe = DuplicatePizzaResultDict(balanced);
                            var scale = Scale(requests, ref dupe, -Math.Sign(deltaMeat), -Math.Sign(deltaVeggie), -Math.Sign(deltaVegan));

                            List<byte> shiftInstr = new List<byte>();
                            bool shouldExpand = false;
                            if (deltaMeat > deltaVegan)
                            {
                                shiftInstr.Add(0b0001_0100);
                                if (deltaMeat == 0)
                                {
                                    shouldExpand = true;
                                }
                            }
                            if (deltaMeat < deltaVegan)
                            {
                                shiftInstr.Add(0b00100_0001);
                                if (deltaMeat == 0)
                                {
                                    shouldExpand = true;
                                }
                            }

                            var deferred = DeferredScale(requests, ref balanced, scale.bestPenalty, shouldExpand, shiftInstr);

                            if (!deferred.foundBetter)
                            {
                                deltaMeat += scale.deltaMeat;
                                deltaVeggie += scale.deltaVeggie;
                                deltaVegan += scale.deltaVegan;

                                balanced = dupe;
                            }
                            else
                            {
                                deltaMeat += deferred.deltaMeat;
                                deltaVeggie += deferred.deltaVeggie;
                                deltaVegan += deferred.deltaVegan;
                            }

                            if ((scale.deltaMeat == 0 && scale.deltaVeggie == 0 && scale.deltaVegan == 0) && (deferred.deltaMeat == 0 && deferred.deltaVeggie == 0 && deferred.deltaVegan == 0)) ;
                        }
                    }
                    */
                }

            }

            float num = balanced.Count;
            float avgPenalty = 0;
            float maxPenalty = float.MinValue;
            foreach (var result in balanced)
            {
                var penaltyVal = CalculatePenalty(requests[result.Key], result.Value);
                result.Value.penaltyMeatVeggi = penaltyVal.penalty;

                avgPenalty += penaltyVal.penalty / num;
                if (penaltyVal.penalty > maxPenalty)
                {
                    maxPenalty = penaltyVal.penalty;
                }
            }

            if (maxPenalty == 0)
            {
                return (null, float.MaxValue, float.MaxValue);
            }

            return (balanced, maxPenalty, avgPenalty);
        }


        private Dictionary<int, PizzaResult> DuplicatePizzaResultDict(Dictionary<int, PizzaResult> original)
        {
            Dictionary<int, PizzaResult> duplicate = new Dictionary<int, PizzaResult>();
            foreach (var result in original)
            {
                var resultCopy = new PizzaResult();
                resultCopy.Id = result.Value.Id;
                resultCopy.resPiecesMeat = result.Value.resPiecesMeat;
                resultCopy.resPiecesVegetarian = result.Value.resPiecesVegetarian;
                resultCopy.resPiecesVegan = result.Value.resPiecesVegan;
                resultCopy.hasPaid = result.Value.hasPaid;
                resultCopy.totalCost = result.Value.totalCost;
                resultCopy.penaltyMeatVeggi = result.Value.penaltyMeatVeggi;
                resultCopy.penaltyVeggieVegan = result.Value.penaltyVeggieVegan;

                duplicate.Add(result.Key, resultCopy);
            }

            return duplicate;
        }

        //balance operations

        //-----tuxic-----
        // try improving balance using on compression/expansion of allowed categories (p/v),
        // may allow defering the operation to the other category and moving over.
        // return deltas (both zero if failed)
        // -----tuxic-----
        private (int deltaMeat, int deltaVeggie, int deltaVegan, float bestPenalty) Scale(Dictionary<int, PizzaRequest> requests, ref Dictionary<int, PizzaResult> resultsIn, int scaleMeat, int scaleVeggie, int scaleVegan)
        {
            Dictionary<int, PizzaResult> results;

            int bestId = -1;
            float bestPenalty = float.MaxValue;
            PizzaResult bestResult = null;

            int deltaMeat = 0, deltaVeggie = 0, deltaVegan = 0;

            //adds delta to result of every order
            if (scaleMeat != 0)
            {
                results = DuplicatePizzaResultDict(resultsIn);
                foreach (var result in results)
                {
                    int value = result.Value.resPiecesMeat + scaleMeat;
                    if (value >= 0)
                    {
                        result.Value.resPiecesMeat = value;
                    }
                    else
                    {
                        continue;
                    }

                    var penResult = CalculatePenalty(requests[result.Key], result.Value);
                    if (penResult.isOk && penResult.penalty < bestPenalty)
                    {
                        bestId = result.Key;
                        bestPenalty = penResult.penalty;
                        bestResult = result.Value;

                        deltaMeat = scaleMeat;
                        deltaVeggie = 0;
                        deltaVegan = 0;
                    }
                }
            }

            //adds delta to result of every order
            if (scaleVeggie != 0)
            {
                results = DuplicatePizzaResultDict(resultsIn);
                foreach (var result in results)
                {
                    int value = result.Value.resPiecesVegetarian + scaleVeggie;
                    if (value >= 0)
                    {
                        result.Value.resPiecesVegetarian = value;
                    }
                    else
                    {
                        continue;
                    }

                    var penResult = CalculatePenalty(requests[result.Key], result.Value);
                    if (penResult.isOk && penResult.penalty < bestPenalty)
                    {
                        bestId = result.Key;
                        bestPenalty = penResult.penalty;
                        bestResult = result.Value;

                        deltaMeat = 0;
                        deltaVeggie = scaleVeggie;
                        deltaVegan = 0;
                    }
                }
            }

            //adds delta to result of every order
            if (scaleVegan != 0)
            {
                results = DuplicatePizzaResultDict(resultsIn);
                foreach (var result in results)
                {
                    int value = result.Value.resPiecesVegan + scaleVegan;
                    if (value >= 0)
                    {
                        result.Value.resPiecesVegan = value;
                    }
                    else
                    {
                        continue;
                    }

                    var penResult = CalculatePenalty(requests[result.Key], result.Value);
                    if (penResult.isOk && penResult.penalty < bestPenalty)
                    {
                        bestId = result.Key;
                        bestPenalty = penResult.penalty;
                        bestResult = result.Value;

                        deltaMeat = 0;
                        deltaVeggie = 0;
                        deltaVegan = scaleVegan;
                    }
                }
            }

            if (bestId >= 0)
            {
                resultsIn[bestId] = bestResult;
            }

            return (deltaMeat, deltaVeggie, deltaVegan, bestPenalty);
        }
        private (bool foundBetter, int deltaMeat, int deltaVeggie, int deltaVegan, float bestPenalty) DeferredScale(Dictionary<int, PizzaRequest> requests, ref Dictionary<int, PizzaResult> resultsIn, float bestPenaltyIn, bool expand, List<byte> shiftFromTo)
        {
            Dictionary<int, PizzaResult> duplicate;

            bool foundBetter = false;

            int deltaMeat = 0, deltaVeggie = 0, deltaVegan = 0;
            float bestPenalty = bestPenaltyIn;

            Dictionary<int, PizzaResult> bestResult = new Dictionary<int, PizzaResult>();

            foreach (var shiftInstruction in shiftFromTo)
            {
                duplicate = DuplicatePizzaResultDict(resultsIn);

                //find best move
                var move = Shift(requests, ref duplicate, shiftInstruction, false);
                //find best scale
                byte scBitmap = shiftInstruction;
                int baseScale = -1;
                if (expand)
                {
                    baseScale = 1;
                    scBitmap >>= 4;
                }
                int scMeat = (scBitmap & 0b0000_0001) > 0 ? baseScale : 0;
                int scVeggie = (scBitmap & 0b0000_0010) > 0 ? baseScale : 0;
                int scVegan = (scBitmap & 0b0000_0100) > 0 ? baseScale : 0;
                var scale = Scale(requests, ref duplicate, scMeat, scVeggie, scVegan);

                float maxPenalty = Math.Max(move.bestPenalty, scale.bestPenalty);

                if (maxPenalty < bestPenalty)
                {
                    foundBetter = true;

                    deltaMeat = move.deltaMeat + scale.deltaMeat;
                    deltaVeggie = move.deltaVeggie + scale.deltaVeggie;
                    deltaVegan = move.deltaVegan + scale.deltaVegan;
                    bestPenalty = maxPenalty;
                    bestResult = duplicate;
                }
            }

            if(foundBetter)
            {
                resultsIn = bestResult;
            }

            return (foundBetter, deltaMeat, deltaVeggie, deltaVegan, bestPenalty);
        }

        private (int deltaMeat, int deltaVeggie, int deltaVegan, float bestPenalty) HandleDiffFillDrainThree(Dictionary<int, PizzaRequest> requests, ref Dictionary<int, PizzaResult> resultsIn, int wishDeltaMeat, int wishDeltaVeggie, int wishDeltaVegan)
        {
            float bestPenalty = float.MaxValue;
            int deltaMeat = 0, deltaVeggie = 0, deltaVegan = 0;

            var bestResult = DuplicatePizzaResultDict(resultsIn);
            int mainDirection = wishDeltaMeat + wishDeltaVeggie + wishDeltaVegan;

            //case1:
            {
                var scale1 = Scale(requests, ref bestResult, wishDeltaMeat, 0, 0);
                var scale2 = Scale(requests, ref bestResult, 0, wishDeltaVeggie, 0);
                var scale3 = Scale(requests, ref bestResult, 0, 0, wishDeltaVegan);

                float maxPen = Math.Max(scale1.bestPenalty, Math.Max(scale2.bestPenalty, scale3.bestPenalty));

                if (maxPen < bestPenalty)
                {
                    bestPenalty = maxPen;
                    deltaMeat = scale1.deltaMeat + scale2.deltaMeat + scale3.deltaMeat;
                    deltaVeggie = scale1.deltaVeggie + scale2.deltaVeggie + scale3.deltaVeggie;
                    deltaVegan = scale1.deltaVegan + scale2.deltaVegan + scale3.deltaVegan;
                }
            }


            //case2 & 3:
            byte shiftOrigin;
            byte shiftDestination;
            //drain 1 fill 2
            if (mainDirection > 0)
            {
                shiftOrigin = (byte)(Math.Max(0, -wishDeltaMeat) * 0b0001_0000 + Math.Max(0, -wishDeltaVeggie) * 0b0010_0000 + Math.Max(0, -wishDeltaVegan) * 0b0000_0100);
                if (wishDeltaMeat > 0)
                {
                    shiftDestination = 0b0000_0001;
                    var dupe = DuplicatePizzaResultDict(resultsIn);
                    var move = Shift(requests, ref dupe, (byte)(shiftOrigin | shiftDestination), false);
                    var expand = Scale(requests, ref dupe, 0, Math.Max(0, wishDeltaVeggie), Math.Max(0, wishDeltaVegan));

                    float maxPen = Math.Max(move.bestPenalty, expand.bestPenalty);

                    if (maxPen < bestPenalty)
                    {
                        bestPenalty = maxPen;
                        deltaMeat = move.deltaMeat + expand.deltaMeat;
                        deltaVeggie = move.deltaVeggie + expand.deltaVeggie;
                        deltaVegan = move.deltaVegan + expand.deltaVegan;
                        bestResult = dupe;
                    }
                }
                else if (wishDeltaVeggie > 0)
                {
                    shiftDestination = 0b0000_0010;
                    var dupe = DuplicatePizzaResultDict(resultsIn);
                    var move = Shift(requests, ref dupe, (byte)(shiftOrigin | shiftDestination), false);
                    var expand = Scale(requests, ref dupe, Math.Max(0, wishDeltaMeat), 0, Math.Max(0, wishDeltaVegan));

                    float maxPen = Math.Max(move.bestPenalty, expand.bestPenalty);

                    if (maxPen < bestPenalty)
                    {
                        bestPenalty = maxPen;
                        deltaMeat = move.deltaMeat + expand.deltaMeat;
                        deltaVeggie = move.deltaVeggie + expand.deltaVeggie;
                        deltaVegan = move.deltaVegan + expand.deltaVegan;
                        bestResult = dupe;
                    }
                }
                else if (wishDeltaVegan > 0)
                {
                    shiftDestination = 0b0000_0100;
                    var dupe = DuplicatePizzaResultDict(resultsIn);
                    var move = Shift(requests, ref dupe, (byte)(shiftOrigin | shiftDestination), false);
                    var expand = Scale(requests, ref dupe, Math.Max(0, wishDeltaMeat), Math.Max(0, wishDeltaVeggie), 0);

                    float maxPen = Math.Max(move.bestPenalty, expand.bestPenalty);

                    if (maxPen < bestPenalty)
                    {
                        bestPenalty = maxPen;
                        deltaMeat = move.deltaMeat + expand.deltaMeat;
                        deltaVeggie = move.deltaVeggie + expand.deltaVeggie;
                        deltaVegan = move.deltaVegan + expand.deltaVegan;
                        bestResult = dupe;
                    }
                }
            }
            //drain 2 fill 1
            else
            {
                shiftDestination = (byte)(Math.Max(0, wishDeltaMeat) * 0b0000_0001 + Math.Max(0, wishDeltaVeggie) * 0b0000_0010 + Math.Max(0, wishDeltaVegan) * 0b0000_0100);
                if (wishDeltaMeat < 0)
                {
                    shiftOrigin = 0b0001_0000;
                    var dupe = DuplicatePizzaResultDict(resultsIn);
                    var move = Shift(requests, ref dupe, (byte)(shiftOrigin | shiftDestination), false);
                    var compress = Scale(requests, ref dupe, 0, Math.Min(0, wishDeltaVeggie), Math.Min(0, wishDeltaVegan));

                    float maxPen = Math.Max(move.bestPenalty, compress.bestPenalty);

                    if (maxPen < bestPenalty)
                    {
                        bestPenalty = maxPen;
                        deltaMeat = move.deltaMeat + compress.deltaMeat;
                        deltaVeggie = move.deltaVeggie + compress.deltaVeggie;
                        deltaVegan = move.deltaVegan + compress.deltaVegan;
                        bestResult = dupe;
                    }
                }
                if (wishDeltaVeggie < 0)
                {
                    shiftOrigin = 0b0010_0000;
                    var dupe = DuplicatePizzaResultDict(resultsIn);
                    var move = Shift(requests, ref dupe, (byte)(shiftOrigin | shiftDestination), false);
                    var compress = Scale(requests, ref dupe, Math.Min(0, wishDeltaMeat), 0, Math.Min(0, wishDeltaVegan));

                    float maxPen = Math.Max(move.bestPenalty, compress.bestPenalty);

                    if (maxPen < bestPenalty)
                    {
                        bestPenalty = maxPen;
                        deltaMeat = move.deltaMeat + compress.deltaMeat;
                        deltaVeggie = move.deltaVeggie + compress.deltaVeggie;
                        deltaVegan = move.deltaVegan + compress.deltaVegan;
                        bestResult = dupe;
                    }
                }
                if (wishDeltaVegan < 0)
                {
                    shiftOrigin = 0b0100_0000;
                    var dupe = DuplicatePizzaResultDict(resultsIn);
                    var move = Shift(requests, ref dupe, (byte)(shiftOrigin | shiftDestination), false);
                    var compress = Scale(requests, ref dupe, Math.Min(0, wishDeltaMeat), Math.Min(0, wishDeltaVeggie), 0);

                    float maxPen = Math.Max(move.bestPenalty, compress.bestPenalty);

                    if (maxPen < bestPenalty)
                    {
                        bestPenalty = maxPen;
                        deltaMeat = move.deltaMeat + compress.deltaMeat;
                        deltaVeggie = move.deltaVeggie + compress.deltaVeggie;
                        deltaVegan = move.deltaVegan + compress.deltaVegan;
                        bestResult = dupe;
                    }
                }
            }

            resultsIn = bestResult;
            return (deltaMeat, deltaVeggie, deltaVegan, bestPenalty);
        }

        private (int deltaMeat, int deltaVeggie, int deltaVegan, float bestPenalty) HandleDiffFillDrainTwo(Dictionary<int, PizzaRequest> requests, ref Dictionary<int, PizzaResult> resultsIn, int wishDeltaMeat, int wishDeltaVeggie, int wishDeltaVegan)
        {
            float bestPenalty = float.MaxValue;
            int deltaMeat = 0, deltaVeggie = 0, deltaVegan = 0;

            var bestResult = DuplicatePizzaResultDict(resultsIn);

            //case1:
            {
                var dupe = DuplicatePizzaResultDict(resultsIn);

                var scale1 = Scale(requests, ref dupe, wishDeltaMeat, 0, 0);
                var scale2 = Scale(requests, ref dupe, 0, wishDeltaVeggie, 0);
                var scale3 = Scale(requests, ref dupe, 0, 0, wishDeltaVegan);

                //if we do not change anything, disregard for maxPen
                if(scale1.deltaVegan == 0&& scale1.deltaVeggie == 0 && scale1.deltaMeat == 0)
                {
                    scale1.bestPenalty = float.MinValue;
                }
                if(scale2.deltaVegan == 0&& scale2.deltaVeggie == 0 && scale2.deltaMeat == 0)
                {
                    scale2.bestPenalty = float.MinValue;
                }
                if(scale3.deltaVegan == 0&& scale3.deltaVeggie == 0 && scale3.deltaMeat == 0)
                {
                    scale3.bestPenalty = float.MinValue;
                }

                float maxPen = Math.Max(scale1.bestPenalty, Math.Max(scale2.bestPenalty, scale3.bestPenalty));

                if (maxPen < bestPenalty && maxPen > float.MinValue)
                {
                    bestPenalty = maxPen;
                    deltaMeat = scale1.deltaMeat + scale2.deltaMeat + scale3.deltaMeat;
                    deltaVeggie = scale1.deltaVeggie + scale2.deltaVeggie + scale3.deltaVeggie;
                    deltaVegan = scale1.deltaVegan + scale2.deltaVegan + scale3.deltaVegan;
                    bestResult = dupe;
                }
            }

            //case 2:
            {
                var dupe = DuplicatePizzaResultDict(resultsIn);
                byte shift = 0b0000_0000;
                if (wishDeltaMeat > 0)
                {
                    shift |= 0b0000_0001;
                } else if(wishDeltaMeat < 0)
                {
                    shift |= 0b0001_0000;
                }
                if(wishDeltaVeggie > 0)
                {
                    shift |= 0b0000_0010;
                } else if(wishDeltaVeggie < 0)
                {
                    shift |= 0b0010_0000;
                }
                if(wishDeltaVegan > 0)
                {
                    shift |= 0b0000_0100;
                } else if(wishDeltaVegan < 0)
                {
                    shift |= 0b0100_0000;
                }

                var move = Shift(requests, ref dupe, shift, false);

                if (move.bestPenalty < bestPenalty)
                {
                    bestPenalty = move.bestPenalty;
                    deltaMeat = move.deltaMeat;
                    deltaVeggie = move.deltaVeggie;
                    deltaVegan = move.deltaVegan;
                    bestResult = dupe;
                }
            }

            //case 3:
            {
                var dupe = DuplicatePizzaResultDict(resultsIn);
                byte shift = 0b0000_0000;
                int scaleMeat = wishDeltaMeat, scaleVeggie = wishDeltaVeggie, scaleVegan = wishDeltaVegan;
                if (wishDeltaMeat > 0)
                {
                    shift |= 0b0000_0001;
                    scaleMeat = 0;
                }
                else if (wishDeltaMeat == 0)
                {
                    shift |= 0b0001_0000;
                    scaleMeat = 1;
                }
                if (wishDeltaVeggie > 0)
                {
                    shift |= 0b0000_0010;
                    scaleVeggie = 0;
                }
                else if (wishDeltaVeggie == 0)
                {
                    shift |= 0b0010_0000;
                    scaleVeggie = 1;
                }
                if (wishDeltaVegan > 0)
                {
                    shift |= 0b0000_0100;
                    scaleVegan = 0;
                }
                else if (wishDeltaVegan == 0)
                {
                    shift |= 0b0100_0000;
                    scaleVegan = 1;
                }

                var move = Shift(requests, ref dupe, shift, false);
                var scale1 = Scale(requests, ref dupe, scaleMeat, 0, 0);
                var scale2 = Scale(requests, ref dupe, 0, scaleVeggie, 0);
                var scale3 = Scale(requests, ref dupe, 0, 0, scaleVegan);

                //if we do not move anything, disregard for maxPen
                if (scale1.deltaVegan == 0 && scale1.deltaVeggie == 0 && scale1.deltaMeat == 0)
                {
                    scale1.bestPenalty = float.MinValue;
                }
                if (scale2.deltaVegan == 0 && scale2.deltaVeggie == 0 && scale2.deltaMeat == 0)
                {
                    scale2.bestPenalty = float.MinValue;
                }
                if (scale3.deltaVegan == 0 && scale3.deltaVeggie == 0 && scale3.deltaMeat == 0)
                {
                    scale3.bestPenalty = float.MinValue;
                }

                float maxPen = Math.Max(Math.Max(scale1.bestPenalty, Math.Max(scale2.bestPenalty, scale3.bestPenalty)), move.bestPenalty);

                if (maxPen < bestPenalty && maxPen > float.MinValue)
                {
                    bestPenalty = maxPen;
                    deltaMeat = scale1.deltaMeat + scale2.deltaMeat + scale3.deltaMeat + move.deltaMeat;
                    deltaVeggie = scale1.deltaVeggie + scale2.deltaVeggie + scale3.deltaVeggie + move.deltaVeggie;
                    deltaVegan = scale1.deltaVegan + scale2.deltaVegan + scale3.deltaVegan + move.deltaVegan;
                    bestResult = dupe;
                }
            }

            //case 4:
            {
                var dupe = DuplicatePizzaResultDict(resultsIn);
                byte shift = 0b0000_0000;
                int scaleMeat = wishDeltaMeat, scaleVeggie = wishDeltaVeggie, scaleVegan = wishDeltaVegan;
                if (wishDeltaMeat == 0)
                {
                    shift |= 0b0000_0001;
                    scaleMeat = -1;
                }
                else if (wishDeltaMeat < 0)
                {
                    shift |= 0b0001_0000;
                    scaleMeat = 0;
                }
                if (wishDeltaVeggie == 0)
                {
                    shift |= 0b0000_0010;
                    scaleVeggie = -1;
                }
                else if (wishDeltaVeggie < 0)
                {
                    shift |= 0b0010_0000;
                    scaleVeggie = 0;
                }
                if (wishDeltaVegan == 0)
                {
                    shift |= 0b0000_0100;
                    scaleVegan = -1;
                }
                else if (wishDeltaVegan < 0)
                {
                    shift |= 0b0100_0000;
                    scaleVegan = 0;
                }

                var move = Shift(requests, ref dupe, shift, false);
                var scale1 = Scale(requests, ref dupe, scaleMeat, 0, 0);
                var scale2 = Scale(requests, ref dupe, 0, scaleVeggie, 0);
                var scale3 = Scale(requests, ref dupe, 0, 0, scaleVegan);

                //if we do not move anything, disregard for maxPen
                if (scale1.deltaVegan == 0 && scale1.deltaVeggie == 0 && scale1.deltaMeat == 0)
                {
                    scale1.bestPenalty = float.MinValue;
                }
                if (scale2.deltaVegan == 0 && scale2.deltaVeggie == 0 && scale2.deltaMeat == 0)
                {
                    scale2.bestPenalty = float.MinValue;
                }
                if (scale3.deltaVegan == 0 && scale3.deltaVeggie == 0 && scale3.deltaMeat == 0)
                {
                    scale3.bestPenalty = float.MinValue;
                }

                float maxPen = Math.Max(Math.Max(scale1.bestPenalty, Math.Max(scale2.bestPenalty, scale3.bestPenalty)), move.bestPenalty);

                if (maxPen < bestPenalty && maxPen > float.MinValue)
                {
                    bestPenalty = maxPen;
                    deltaMeat = scale1.deltaMeat + scale2.deltaMeat + scale3.deltaMeat + move.deltaMeat;
                    deltaVeggie = scale1.deltaVeggie + scale2.deltaVeggie + scale3.deltaVeggie + move.deltaVeggie;
                    deltaVegan = scale1.deltaVegan + scale2.deltaVegan + scale3.deltaVegan + move.deltaVegan;
                    bestResult = dupe;
                }
            }

            resultsIn = bestResult;
            return (deltaMeat, deltaVeggie, deltaVegan, bestPenalty);
        }

        private (int deltaMeat, int deltaVeggie, int deltaVegan, float bestPenalty) HandleDiffFillDrainOne(Dictionary<int, PizzaRequest> requests, ref Dictionary<int, PizzaResult> resultsIn, int wishDeltaMeat, int wishDeltaVeggie, int wishDeltaVegan)
        {
            float bestPenalty = float.MaxValue;
            int deltaMeat = 0, deltaVeggie = 0, deltaVegan = 0;

            var bestResult = DuplicatePizzaResultDict(resultsIn);

            //case1:
            {
                var dupe = DuplicatePizzaResultDict(resultsIn);

                var scale1 = Scale(requests, ref dupe, wishDeltaMeat, 0, 0);
                var scale2 = Scale(requests, ref dupe, 0, wishDeltaVeggie, 0);
                var scale3 = Scale(requests, ref dupe, 0, 0, wishDeltaVegan);

                //if we do not change anything, disregard for maxPen
                if (scale1.deltaVegan == 0 && scale1.deltaVeggie == 0 && scale1.deltaMeat == 0)
                {
                    scale1.bestPenalty = float.MinValue;
                }
                if (scale2.deltaVegan == 0 && scale2.deltaVeggie == 0 && scale2.deltaMeat == 0)
                {
                    scale2.bestPenalty = float.MinValue;
                }
                if (scale3.deltaVegan == 0 && scale3.deltaVeggie == 0 && scale3.deltaMeat == 0)
                {
                    scale3.bestPenalty = float.MinValue;
                }

                float maxPen = Math.Max(scale1.bestPenalty, Math.Max(scale2.bestPenalty, scale3.bestPenalty));

                if (maxPen < bestPenalty && maxPen > float.MinValue)
                {
                    bestPenalty = maxPen;
                    deltaMeat = scale1.deltaMeat + scale2.deltaMeat + scale3.deltaMeat;
                    deltaVeggie = scale1.deltaVeggie + scale2.deltaVeggie + scale3.deltaVeggie;
                    deltaVegan = scale1.deltaVegan + scale2.deltaVegan + scale3.deltaVegan;
                    bestResult = dupe;
                }
            }

            //case 2:
            {
                int direction = wishDeltaMeat + wishDeltaVeggie + wishDeltaVegan;
                var dupe = DuplicatePizzaResultDict(resultsIn);
                byte shift = 0b0000_0000;
                
                if (wishDeltaMeat != 0)
                {
                    shift |= 0b0000_0001;
                }
                else if (wishDeltaVeggie != 0)
                {
                    shift |= 0b0000_0010;
                }
                else if (wishDeltaVegan != 0)
                {
                    shift |= 0b0000_0100;
                }

                List<byte> shiftInstr = new List<byte>();
                if(direction > 0)
                {
                    if (wishDeltaMeat == 0)
                    {
                        shiftInstr.Add((byte)(shift | 0b0001_0000));
                    }
                    if (wishDeltaVeggie == 0)
                    {
                        shiftInstr.Add((byte)(shift | 0b0010_0000));
                    }
                    if(wishDeltaVegan == 0)
                    {
                        shiftInstr.Add((byte)(shift | 0b0100_0000));
                    }
                }
                else
                {
                    shift <<= 4;
                    
                    if(wishDeltaMeat == 0)
                    {
                        shiftInstr.Add((byte)(shift | 0b0000_0001));
                    }
                    if(wishDeltaVeggie == 0)
                    {
                        shiftInstr.Add((byte)(shift | 0b0000_0010));
                    }
                    if(wishDeltaVegan == 0)
                    {
                        shiftInstr.Add((byte)(shift | 0b0000_0100));
                    }
                }

                var defferedScale = DeferredScale(requests, ref dupe, bestPenalty, direction > 0, shiftInstr);

                if (defferedScale.foundBetter)
                {
                    bestPenalty = defferedScale.bestPenalty;
                    deltaMeat = defferedScale.deltaMeat;
                    deltaVeggie = defferedScale.deltaVeggie;
                    deltaVegan = defferedScale.deltaVegan;
                    bestResult = dupe;
                }
            }

            resultsIn = bestResult;
            return (deltaMeat, deltaVeggie, deltaVegan, bestPenalty);
        }

        private (int deltaMeat, int deltaVeggie, int deltaVegan, float bestPenalty) Shift(Dictionary<int, PizzaRequest> requests, ref Dictionary<int, PizzaResult> resultsIn, byte shiftFromTo, bool allowScaling)
        {
            var results = DuplicatePizzaResultDict(resultsIn);

            byte shiftOrigin = (byte)(shiftFromTo >> 4);
            byte shiftDestination = (byte)(shiftFromTo & 0b0000_0111);

            int bestId = -1;
            float bestPenalty = float.MaxValue;
            PizzaResult bestResult = null;

            int shiftMeat = -(shiftOrigin & 0b0000_0001) + (shiftDestination & 0b0000_0001);
            shiftOrigin >>= 1; shiftDestination >>= 1;
            int shiftVeggie = -(shiftOrigin & 0b0000_0001) + (shiftDestination & 0b0000_0001);
            shiftOrigin >>= 1; shiftDestination >>= 1;
            int shiftVegan = -(shiftOrigin & 0b0000_0001) + (shiftDestination & 0b0000_0001);

            int deltaMeat = 0, deltaVeggie = 0, deltaVegan = 0;

            foreach (var result in results)
            {
                int value = result.Value.resPiecesMeat + shiftMeat;
                if (value >= 0)
                {
                    result.Value.resPiecesMeat = value;
                }
                else
                {
                    continue;
                }

                value = result.Value.resPiecesVegetarian + shiftVeggie;
                if (value >= 0)
                {
                    result.Value.resPiecesVegetarian = value;
                }
                else
                {
                    continue;
                }

                value = result.Value.resPiecesVegan + shiftVegan;
                if (value >= 0)
                {
                    result.Value.resPiecesVegan = value;
                }
                else
                {
                    continue;
                }

                var penResult = CalculatePenalty(requests[result.Key], result.Value);
                if (penResult.isOk && penResult.penalty < bestPenalty)
                {
                    bestId = result.Key;
                    bestPenalty = penResult.penalty;
                    bestResult = result.Value;

                    deltaMeat = shiftMeat;
                    deltaVeggie = shiftVeggie;
                    deltaVegan = shiftVegan;
                }
            }

            if (allowScaling)
            {
                Dictionary<int, PizzaResult> duplicate = DuplicatePizzaResultDict(resultsIn);

                //find best compress
                var compress = Scale(requests, ref duplicate, Math.Min(shiftMeat, 0), Math.Min(shiftVeggie, 0), Math.Min(shiftVegan, 0));
                //find best expand
                var expand = Scale(requests, ref duplicate, Math.Max(shiftMeat, 0), Math.Max(shiftVeggie, 0), Math.Max(shiftVegan, 0));


                // cannot be on the same order (id), or it would not be better than the non-deferred scale
                float maxPen = Math.Max(compress.bestPenalty, expand.bestPenalty);

                if (maxPen < bestPenalty)
                {
                    deltaMeat = compress.deltaMeat + expand.deltaMeat;
                    deltaVeggie = compress.deltaVeggie + expand.deltaVeggie;
                    deltaVegan = compress.deltaVegan + expand.deltaVegan;
                    bestPenalty = maxPen;
                    resultsIn = duplicate;
                }
                else if (bestId >= 0)
                {
                    resultsIn[bestId] = bestResult;
                }
            }
            else if (bestId >= 0)
            {
                resultsIn[bestId] = bestResult;
            }

            return (deltaMeat, deltaVeggie, deltaVegan, bestPenalty);
        }

    }
}
