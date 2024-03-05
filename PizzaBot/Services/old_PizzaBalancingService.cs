using PizzaBot.Models;

namespace PizzaBot.Services
{
    public class old_PizzaBalancingService
    {

        private readonly JSONService _jsonService;
        public old_PizzaBalancingService(JSONService jsonService)
        {
            _jsonService = jsonService;
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

            int reqNum = reqMeat + reqVeggie;
            int resNum = resMeat + resVeggie;

            uint diffNumReqRes = (uint)Math.Abs(reqNum - resNum);

            //result is in 0.01 to sq(diffNum)*catTol*0.99
            float num_base = 0.01f;
            //float penaltyCount = (diffNumReqRes * diffNumReqRes) * (categoryToleranceMeatVeggie * (1 - num_base) + num_base);


            //float epsilon = 0.001f;
            //float percentMeatInRequest = reqMeat / (reqNum + epsilon);
            //float percentMeatInResult = resMeat / (resNum + epsilon);

            //float diffMeatShareResReq = percentMeatInResult - percentMeatInRequest;
            //float penaltyCat = (diffMeatShareResReq * diffMeatShareResReq) / (categoryToleranceMeatVeggie + epsilon);

            //float f = 0.5f; // fav: 0.0: num, 1.0: cat
            //float penaltyMeatVeggi = ((1.0f - f) * penaltyCount + f * penaltyCat) / reqNum;

            // ________Vanessa Penalties________
            float penaltyCount = (Math.Abs(reqNum - resNum)) / (float)reqNum; // 0 - 1
                                                                              // apply function to make higher differences have higher penalty
            penaltyCount = (float)Math.Pow(penaltyCount, 2);

            float epsilon = 0.001f;
            float percentMeatInRequest = reqMeat / (reqNum + epsilon); // 0 - 1
            float percentMeatInResult = resMeat / (resNum + epsilon); // 0 - 1

            float percentVeggiInRequest = reqMeat / (reqNum + epsilon);
            float percentVeggiInResult = resMeat / (resNum + epsilon);

            float diffMeatShareResReq = Math.Abs(percentMeatInResult - percentMeatInRequest); // 0 - 1
            float diffVeggiShareResReq = Math.Abs(percentVeggiInResult - percentVeggiInRequest);

            float penaltyCat = (diffMeatShareResReq + diffVeggiShareResReq) / 2; // 0 - 1
                                                                                 // apply function to make small differences have higher penalty
            penaltyCat = (float)Math.Sqrt(penaltyCat);


            // take weighted average of both penalties using categoryToleranceMeatVeggie
            float f = categoryToleranceMeatVeggie; // fav: 0.0: num, 1.0: cat
            float penaltyMeatVeggi = (1.0f - f) * penaltyCount + f * penaltyCat;
            // ________Vanessa Penalties________


            //float f = 0.5f; // fav: 0.0: num, 1.0: cat
            //float penaltyMeatVeggi = ((1.0f - f) * penaltyCount + f * penaltyCat) / reqNum;

            //only ok, if at least one piece of each requested type has been assigned
            bool meatOk = !(reqMeat == 0 && resMeat != 0);
            bool veggieOk = !(reqVeggie == 0 && resVeggie != 0);

            return (penaltyMeatVeggi, categoryToleranceMeatVeggie != 0.0f || (meatOk && veggieOk));
        }

        public (Dictionary<int, PizzaResult> results, int requiredMeat, int requiredVeggie, int requiredVegan, float totalCost) Distribute(Dictionary<int, PizzaRequest> orders)
        {
            PizzaConfig config = _jsonService.ReadPizzaConfig();
            //PizzaConfig config = new PizzaConfig();
            //config.Fragments = 15;
            //config.Price = 2150;


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

            var balanced = Balance(orders, config.Fragments, false, false);
            Console.WriteLine("chose false, false" + balanced);
            var newBalanced = Balance(orders, config.Fragments, false, true);
            if (compareResults(balanced.maxPen, balanced.avgPen, newBalanced.maxPen, newBalanced.avgPen))
            {
                balanced = newBalanced;
                Console.WriteLine("chose false, true" + balanced);
            }
            newBalanced = Balance(orders, config.Fragments, true, false);
            if (compareResults(balanced.maxPen, balanced.avgPen, newBalanced.maxPen, newBalanced.avgPen))
            {
                balanced = newBalanced;
                Console.WriteLine("chose true, false" + balanced);

            }
            newBalanced = Balance(orders, config.Fragments, true, true);
            if (compareResults(balanced.maxPen, balanced.avgPen, newBalanced.maxPen, newBalanced.avgPen))
            {
                balanced = newBalanced;
                Console.WriteLine("chose true, true" + balanced);

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
                p = (result.Value.resPiecesMeat + result.Value.resPiecesVegetarian) * config.Price;
                p = (p + config.Fragments - 1) / config.Fragments;
                result.Value.totalCost = p * 0.01f;
                requiredMeat += result.Value.resPiecesMeat;
                requiredVeggie += result.Value.resPiecesVegetarian;
            }
            if (requiredMeat % config.Fragments != 0)
            {
                throw new Exception("Meat fragments don't fit!");
            }
            if (requiredVeggie % config.Fragments != 0)
            {
                throw new Exception("Veggie fragments don't fit!");
            }

            requiredMeat /= config.Fragments;
            requiredVeggie /= config.Fragments;

            float totalCost = (requiredMeat + requiredVeggie + requiredVegan) * config.Price / 100.0f;

            return (balanced.balanced, requiredMeat, requiredVeggie, 0, totalCost);
        }

        public (Dictionary<int, PizzaResult> balanced, float maxPen, float avgPen) Balance(Dictionary<int, PizzaRequest> requests, int piecesPerPizza, bool fillMeat, bool fillVeggie)
        {
            Dictionary<int, PizzaResult> balanced = new Dictionary<int, PizzaResult>();

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

            //determine delta of pieces to next viable pizza order
            int deltaMeat = numMeat - requiredMeatPizzas * piecesPerPizza;
            int deltaVeggie = numVeggie - requiredVeggiePizzas * piecesPerPizza;

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

            if (!fillMeat && !fillVeggie)
            {
                while (!(deltaMeat == 0 || deltaVeggie == 0))
                {
                    // (best) compress any category
                    var scale = Scale(requests, ref balanced, false, true, true, false);
                    deltaMeat += scale.deltaMeat;
                    deltaVeggie += scale.deltaVeggie;

                    if (scale.deltaMeat == 0 && scale.deltaVeggie == 0)
                    {
                        break;
                    }
                }
                while (!(deltaMeat == 0 && deltaVeggie == 0))
                {
                    // (best) compress !fitting, allow deferred
                    var scale = Scale(requests, ref balanced, false, deltaMeat != 0, deltaVeggie != 0, true);
                    deltaMeat += scale.deltaMeat;
                    deltaVeggie += scale.deltaVeggie;

                    if (scale.deltaMeat == 0 && scale.deltaVeggie == 0)
                    {
                        break;
                    }
                }
            }
            else if (fillMeat && fillVeggie)
            {
                while (!(deltaMeat == 0 || deltaVeggie == 0))
                {
                    // (best) expand any category
                    var scale = Scale(requests, ref balanced, true, true, true, false);
                    deltaMeat += scale.deltaMeat;
                    deltaVeggie += scale.deltaVeggie;

                    if (scale.deltaMeat == 0 && scale.deltaVeggie == 0)
                    {
                        break;
                    }
                }
                while (!(deltaMeat == 0 && deltaVeggie == 0))
                {
                    // (best)  expand !fitting, allow deferred
                    var scale = Scale(requests, ref balanced, true, deltaMeat != 0, deltaVeggie != 0, true);
                    deltaMeat += scale.deltaMeat;
                    deltaVeggie += scale.deltaVeggie;

                    if (scale.deltaMeat == 0 && scale.deltaVeggie == 0)
                    {
                        break;
                    }
                }
            }
            else
            {
                while (!(deltaMeat == 0 || deltaVeggie == 0))
                {
                    var shift = Shift(requests, ref balanced, fillVeggie, true);
                    if (shift.bestPenalty == float.MaxValue)
                    {
                        return (null, shift.bestPenalty, shift.bestPenalty);
                    }

                    deltaMeat += shift.deltaMeat;
                    deltaVeggie += shift.deltaVeggie;

                    if (shift.deltaMeat == 0 && shift.deltaVeggie == 0)
                    {
                        break;
                    }
                }
                int deltaFill = fillMeat ? deltaMeat : deltaVeggie;
                bool fillFits = deltaFill == 0;
                while (!(deltaMeat == 0 && deltaVeggie == 0))
                {
                    var scale = Scale(requests, ref balanced, !fillFits, deltaMeat != 0, deltaVeggie != 0, true);
                    if (scale.bestPenalty == float.MaxValue)
                    {
                        return (null, scale.bestPenalty, scale.bestPenalty);
                    }

                    deltaMeat += scale.deltaMeat;
                    deltaVeggie += scale.deltaVeggie;

                    if (scale.deltaMeat == 0 && scale.deltaVeggie == 0)
                    {
                        break;
                    }
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
        private (int deltaMeat, int deltaVeggie, float bestPenalty) Scale(Dictionary<int, PizzaRequest> requests, ref Dictionary<int, PizzaResult> resultsIn, bool expand, bool meat, bool veggie, bool allow_deferred)
        {
            var results = DuplicatePizzaResultDict(resultsIn);

            int bestId = -1;
            float bestPenalty = float.MaxValue;
            PizzaResult bestResult = null;

            int delta = expand ? 1 : -1;

            int deltaMeat = 0, deltaVeggie = 0;

            //adds delta to result of every order
            if (meat)
            {
                foreach (var result in results)
                {
                    int value = result.Value.resPiecesMeat + delta;
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

                        deltaMeat = delta;
                        deltaVeggie = 0;
                    }
                }
            }

            results = DuplicatePizzaResultDict(resultsIn);
            //adds delta to result of every order
            if (veggie)
            {
                foreach (var result in results)
                {
                    int value = result.Value.resPiecesVegetarian + delta;
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
                        deltaVeggie = delta;
                    }
                }
            }


            if (allow_deferred && (meat != veggie))
            {
                //duplicate results
                Dictionary<int, PizzaResult> duplicate = DuplicatePizzaResultDict(resultsIn);

                //find best move
                var move = Shift(requests, ref duplicate, meat != expand, false);
                //find best scale
                var scale = Scale(requests, ref duplicate, expand, !meat, !veggie, false);

                float maxPenalty = Math.Max(move.bestPenalty, scale.bestPenalty);

                if (maxPenalty < bestPenalty)
                {
                    deltaMeat = move.deltaMeat + scale.deltaMeat;
                    deltaVeggie = move.deltaMeat + scale.deltaVeggie;
                    bestPenalty = maxPenalty;
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

            return (deltaMeat, deltaVeggie, bestPenalty);
        }

        private (int deltaMeat, int deltaVeggie, float bestPenalty) Shift(Dictionary<int, PizzaRequest> requests, ref Dictionary<int, PizzaResult> resultsIn, bool toVeggie, bool allowScaling)
        {
            var results = DuplicatePizzaResultDict(resultsIn);

            int bestId = -1;
            float bestPenalty = float.MaxValue;
            PizzaResult bestResult = null;

            int delta = toVeggie ? -1 : 1;

            int deltaMeat = 0, deltaVeggie = 0;

            foreach (var result in results)
            {
                int value = result.Value.resPiecesMeat + delta;
                if (value >= 0)
                {
                    result.Value.resPiecesMeat = value;
                }
                else
                {
                    continue;
                }

                value = result.Value.resPiecesVegetarian - delta;
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

                    deltaMeat = delta;
                    deltaVeggie = -delta;
                }
            }

            if (allowScaling)
            {
                Dictionary<int, PizzaResult> duplicate = DuplicatePizzaResultDict(resultsIn);

                //find best compress
                var compress = Scale(requests, ref duplicate, false, toVeggie, !toVeggie, false);
                //find best expand
                var expand = Scale(requests, ref duplicate, true, !toVeggie, toVeggie, false);


                // cannot be on the same order (id), or it would not be better than the non-deferred scale
                float maxPen = Math.Max(compress.bestPenalty, expand.bestPenalty);

                if (maxPen < bestPenalty)
                {
                    deltaMeat = compress.deltaMeat + expand.deltaMeat;
                    deltaVeggie = compress.deltaVeggie + expand.deltaVeggie;
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

            return (deltaMeat, deltaVeggie, bestPenalty);
        }

    }
}
