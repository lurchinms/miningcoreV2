#include <vector>

#ifndef POWSCHEME_H
#define POWSCHEME_H

#include "blake/blake2.h"


enum SolverCancelCheck
{
    ListGeneration,
    ListSorting,
    ListColliding,
    RoundEnd,
    FinalSorting,
    FinalColliding,
    PartialGeneration,
    PartialSorting,
    PartialSubtreeEnd,
    PartialIndexEnd,
    PartialEnd,
    MixElements
};

class SolverCancelledException : public std::exception
{
    virtual const char* what() const throw()
    {
        return "BeamHash solver was cancelled";
    }
};


class PoWScheme
{
public:
    virtual int InitialiseState(blake2b_state& base_state) = 0;
    virtual bool IsValidSolution(const blake2b_state& base_state, std::vector<unsigned char> soln) = 0;
    virtual bool OptimisedSolve(const blake2b_state& base_state,
        const std::function<bool(const std::vector<unsigned char>&)> validBlock,
        const std::function<bool(SolverCancelCheck)> cancelled) = 0;
};

#endif