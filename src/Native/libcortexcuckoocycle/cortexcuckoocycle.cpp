#include "cortexcuckoocycle.hpp"
#include <stdint.h>

#include "cuckaroo/cuckaroo.hpp"

int32_t cortexcuckoocycle(const char *header, int headerLen, const char *solution)
{
    siphash_keys keys;
    cuckaroo_cortex_setheader(header, headerLen, &keys);
    int res = cuckaroo_cortex_verify((cuckaroo_cortex_word_t* )solution, keys);
    return res;
}
