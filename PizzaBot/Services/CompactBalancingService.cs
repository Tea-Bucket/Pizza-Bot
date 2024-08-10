using System.Diagnostics;
using PizzaBot.Models;

namespace PizzaBot.Services {
    public enum PizzaKind {
        Meat,
        Vegetarian,
        Vegan
    }


    public struct PizzaKindArray<T> {
        public T meat;
        public T vegetarian;
        public T vegan;

        public T this[PizzaKind kind] {
            readonly get => kind switch
            {
                PizzaKind.Meat => this.meat,
                PizzaKind.Vegetarian => this.vegetarian,
                PizzaKind.Vegan => this.vegan,
                _ => throw new UnreachableException(),
            };
            set {
                switch(kind)
                {
                    case PizzaKind.Meat: this.meat = value; break;
                    case PizzaKind.Vegetarian: this.vegetarian = value; break;
                    case PizzaKind.Vegan: this.vegan = value; break;
                }
            }
        }

        public static PizzaKindArray<T> Splat(T value) => new() {
            meat = value,
            vegetarian = value,
            vegan = value
        };

        public readonly PizzaKindArray<S> Map<S>(Func<T, S> func) => new() {
            meat = func(this.meat),
            vegetarian = func(this.vegetarian),
            vegan = func(this.vegan),
        };

        public readonly S Reduce<S>(S initial, Func<S, T, S> func) {
            initial = func(initial, this.meat);
            initial = func(initial, this.vegetarian);
            initial = func(initial, this.vegan);
            return initial;
        }

        public readonly PizzaKindArray<R> ZipMap<S, R>(PizzaKindArray<S> other, Func<T, S, R> func) => new() {
            meat = func(this.meat, other.meat),
            vegetarian = func(this.vegetarian, other.vegetarian),
            vegan = func(this.vegan, other.vegan),
        };

        public void ForEachRef<S>(PizzaKindArray<S> other, RefFirst<S> func) {
            func(ref this.meat, other.meat);
            func(ref this.vegetarian, other.vegetarian);
            func(ref this.vegan, other.vegan);
        }

        public readonly void ForEach<S>(PizzaKindArray<S> other, Action<T, S> func) {
            func(this.meat, other.meat);
            func(this.vegetarian, other.vegetarian);
            func(this.vegan, other.vegan);
        }

        public delegate void RefFirst<S>(ref T first, S second);
    }

    public struct Request {
        public PizzaKindArray<uint> distribution;
        public float preference;

        public readonly float CalculateCost(PizzaKindArray<uint> distribution) {
            const float epsilon = 0.0001f;

            var pref = 1 - this.preference;
            var count_pref = (1 - pref) / pref + 0.01f;
            var shape_pref = pref / (1 - pref) + 0.01f;

            var r_total = this.distribution.Reduce(0u, (a, b) => a + b);
            var a_total = distribution.Reduce(0u, (a, b) => a + b);

            var total_diff = r_total > a_total ? r_total - a_total : a_total - r_total;
            static float Convert(float diff, float total, float p, bool more) {
                var perc_of_total = diff / total;
                if (more) {
                    var scaled = 1 / (1 - (1 - 1 / (2 * total)) * (perc_of_total - 1)) - 1;
                    if (scaled < 0) {
                        scaled = float.PositiveInfinity;
                    }
                    return scaled * p;
                } else {
                    var scaled = perc_of_total / Math.Max(1 - perc_of_total, 0);
                    return scaled * p;
                }
            }
            var total_pentalty = total_diff == 0 ? 0 : Convert(total_diff, r_total, count_pref, a_total > r_total);

            static PizzaKindArray<float> PrepareValues(PizzaKindArray<uint> values, uint total) => values.Map(v => (float)v / total);

            var r_perc = PrepareValues(this.distribution, r_total);
            var a_perc = PrepareValues(distribution, a_total);

            var diffs = r_perc.ZipMap(a_perc, (r, a) => r > a ? r - a : a - r);
            var scaled_diffs = diffs.Map(d => d * shape_pref);
            var pens = diffs.ZipMap(scaled_diffs, (d, s) => d < epsilon ? d : s);

            return total_pentalty + 1f / CompactBalance.Length * pens.Reduce(0f, (a, v) => a + v);
        }
    }

    public static class CompactBalance {
        public const uint Length = 3;

        private struct QueueElement {
            public uint request_index;
            public PizzaKindArray<bool> offset;
            public float penalty;

            public static QueueElement? BestOffset(PizzaKindArray<bool> adds, PizzaKindArray<uint> deltas, Request request, PizzaKindArray<uint> assigned, uint index) {
                PizzaKindArray<bool>? best = null;
                var penalty = float.PositiveInfinity;
                for (var idx = 1u; idx < (1 << (int)Length); idx++) {
                    var modify = PizzaKindArray<bool>.Splat(false);
                    for (PizzaKind ty = 0; (uint)ty < Length; ty++) {
                        var mod = (idx & (1 << (int)ty)) != 0;
                        if (deltas[ty] == 0 && mod)
                            goto outer_cont;
                        modify[ty] = mod;
                    }

                    var copy = assigned;
                    for (PizzaKind i = 0; (uint)i < Length; i++) {
                        if (modify[i]) {
                            if (adds[i]) {
                                copy[i] += 1;
                            } else {
                                if (copy[i] == 0) goto outer_cont;
                                copy[i] -= 1;
                            }
                        }
                    }

                    var pen = request.CalculateCost(copy);

                    if (pen < penalty) {
                        penalty = pen;
                        best = modify;
                    }

                    outer_cont:
                    {}
                }

                if (best is not PizzaKindArray<bool> best_ty)
                    return null;

                return new QueueElement {
                    request_index = index,
                    offset = best_ty,
                    penalty = penalty
                };
            }
        };

        private struct TotalPenalty {
            public float worst;
            public float average;

            public TotalPenalty() {
                worst = 0;
                average = 0;
            }

            public void Add(float penalty) {
                this.worst = Math.Max(this.worst, penalty);
                this.average += penalty;
            }

            public bool IsBetterThan(TotalPenalty that) {
                var this_penalty = this.Total();
                var that_penalty = that.Total();

                if (this_penalty < that_penalty)
                    return true;
                if (that_penalty < this_penalty)
                    return false;

                if (this.average < that.average)
                    return true;
                if (that.average < this.average)
                    return false;

                return true;
            }

            public float Total() {
                const float weight = 0.1f;
                return (1 - weight) * this.worst + weight * this.average;
            }
        }

        private static (TotalPenalty penalty, PizzaKindArray<uint> config, PizzaKindArray<uint>[] distribution, bool is_valid) GetBest(uint pieces_per_whole, Request[] requests) {
            var totals = PizzaKindArray<uint>.Splat(0);
            foreach (var req in requests) {
                for (PizzaKind i = 0; (uint)i < Length; i++) {
                    totals[i] += req.distribution[i];
                }
            }

            var queue = new PriorityQueue<QueueElement, float>();

            var best_distribution = new PizzaKindArray<uint>[requests.Length];
            var best_config = PizzaKindArray<bool>.Splat(false);
            var penalty = new TotalPenalty {
                worst = float.PositiveInfinity,
                average = float.PositiveInfinity,
            };

            var next_distr = new PizzaKindArray<uint>[requests.Length];

            for (var index = 0u; index < 1 << (int)Length; index++) {
                var adds = PizzaKindArray<bool>.Splat(false);
                for (PizzaKind ty = 0; (uint)ty < Length; ty++) {
                    var increase = (index & (1 << (int)ty)) != 0;
                    if (totals[ty] % pieces_per_whole == 0 && increase)
                        goto outer_cont;
                    adds[ty] = increase;
                }

                var deltas = new PizzaKindArray<uint>();
                for (PizzaKind i = 0; (uint)i < Length; i++) {
                    var target = (totals[i] / pieces_per_whole) * pieces_per_whole;
                    if (adds[i]) {
                        target += pieces_per_whole;
                        deltas[i] = target - totals[i];
                    } else {
                        deltas[i] = totals[i] - target;
                    }
                }

                queue.Clear();
                for (uint i = 0; i < requests.Length; i++) {
                    next_distr[i] = requests[i].distribution;
                    if (QueueElement.BestOffset(adds, deltas, requests[i], requests[i].distribution, i) is QueueElement best) {
                        queue.Enqueue(best, best.penalty);
                    }
                }

                var pen = new TotalPenalty();
                while (deltas.Reduce(0u, (a, d) => a + d) != 0) {
                    if (!queue.TryDequeue(out var element, out _)) goto outer_cont;

                    for (PizzaKind i = 0; (uint)i < Length; i++) {
                        if (element.offset[i] && deltas[i] == 0) goto enqueue;
                    }
                    for (PizzaKind i = 0; (uint)i < Length; i++) {
                        if (element.offset[i]) {
                            if (adds[i]) {
                                next_distr[element.request_index][i] += 1;
                            } else {
                                next_distr[element.request_index][i] -= 1;
                            }
                            deltas[i] -= 1;
                            pen.Add(element.penalty);
                        }
                    }

                    enqueue:
                    if (QueueElement.BestOffset(adds, deltas, requests[element.request_index], next_distr[element.request_index], element.request_index) is QueueElement best) {
                        queue.Enqueue(best, best.penalty);
                    }
                }

                if (pen.IsBetterThan(penalty)) {
                    penalty = pen;
                    best_config = adds;
                    (next_distr, best_distribution) = (best_distribution, next_distr);
                }

                outer_cont:
                {}
            }

            var config = new PizzaKindArray<uint>();
            for (PizzaKind i = 0; (uint)i < Length; i++) {
                var target = totals[i] / pieces_per_whole;
                if (best_config[i]) {
                    target += 1;
                }
                config[i] = target;
            }

            return (penalty, config, distribution: best_distribution, !float.IsPositiveInfinity(penalty.worst));
        }

        public static (Dictionary<int, PizzaResult> results, PizzaKindArray<uint> config) Distribute(Dictionary<int, PizzaRequest> orders, uint pieces_per_whole) {
            var indices = new int[orders.Count];
            var requests = new Request[orders.Count];

            {
                var i = 0;
                foreach (var (index, request) in orders) {
                    indices[i] = index;
                    requests[i] = new Request {
                        distribution = new() {
                            meat = (uint)request.reqPiecesMeat,
                            vegetarian = (uint)request.reqPiecesVegetarian,
                            vegan = (uint)request.reqPiecesVegan,
                        },
                        preference = request.priority
                    };
                    i += 1;
                }
            }

            var (_, config, distribution, is_valid) = GetBest(pieces_per_whole, requests);

            if (!is_valid) {
                for (var i = 0; i < distribution.Length; i++) {
                    distribution[i] = PizzaKindArray<uint>.Splat(0);
                }

                config = PizzaKindArray<uint>.Splat(0);
            }

            var results = new Dictionary<int, PizzaResult>(requests.Length);
            for (var i = 0; i < requests.Length; i++) {
                results.Add(indices[i], new() {
                    Id = indices[i],
                    resPiecesMeat = (int)distribution[i].meat,
                    resPiecesVegetarian = (int)distribution[i].vegetarian,
                    resPiecesVegan = (int)distribution[i].vegan,
                });
            }

            return (results, config);
        }
    }
}