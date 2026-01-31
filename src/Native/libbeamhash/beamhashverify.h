#ifndef BEAMHASHVERIFY_H
#define BEAMHASHVERIFY_H

#include "beamHashIII.h"
#include "equihashR.h"
#include <string>
#include <vector>

#ifdef __cplusplus
extern "C" {
#endif

bool verifyBH(const char*,
    const char*,
    const std::vector<unsigned char>&,
    int pow);

#ifdef __cplusplus
}
#endif

#endif
