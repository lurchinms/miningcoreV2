#include <cmath>
#include <stdint.h>
#include <string>
#include <algorithm>
#include "currency_core/currency_basic.h"
#include "currency_core/currency_format_utils.h"
#include "currency_protocol/blobdatatype.h"
#include "crypto/crypto.h"
#include "crypto/hash.h"
#include "common/base58.h"
#include "serialization/binary_utils.h"
#include "currency_core/basic_pow_helpers.h"

using namespace currency;

#ifdef _WIN32
#define MODULE_API __declspec(dllexport)
#else
#define MODULE_API
#endif

extern "C" MODULE_API bool convert_blob_export(const char* input, unsigned int inputSize, unsigned char *output, unsigned int *outputSize)
{
    unsigned int originalOutputSize = *outputSize;

    blobdata input_blob = std::string(input, inputSize);
    blobdata result = "";

    currency::block block = AUTO_VAL_INIT(block);
    if (!currency::parse_and_validate_block_from_blob(input_blob, block))
    {
        *outputSize = 0;
        return false;
    }

    // now hash it
    result = currency::get_block_hashing_blob(block);
    *outputSize = (int) result.length();

    // output buffer big enough?
    if (result.length() > originalOutputSize)
        return false;

    // success
    memcpy(output, result.data(), result.length());
    return true;
}

extern "C" MODULE_API bool get_blob_id_export(const char* input, unsigned int inputSize, unsigned char *output)
{
    blobdata input_blob = std::string(input, inputSize);
    crypto::hash result;

    currency::block block = AUTO_VAL_INIT(block);
    if (!currency::parse_and_validate_block_from_blob(input_blob, block))
        return false;

    result = currency::get_block_header_mining_hash(block);

    char *cstr = reinterpret_cast<char *>(&result);

    // success
    memcpy(output, cstr, 32);
    return true;
}

extern "C" MODULE_API bool convert_block_export(const char* input, unsigned int inputSize, unsigned char *output, unsigned int *outputSize, uint64_t nonce)
{
    unsigned int originalOutputSize = *outputSize;

    blobdata input_blob = std::string(input, inputSize);
    blobdata result = "";
    //crypto::hash result;

    currency::block block = AUTO_VAL_INIT(block);
    if (!currency::parse_and_validate_block_from_blob(input_blob, block))
    {
        *outputSize = 0;
        return false;
    }

    block.nonce = nonce;

    result = currency::block_to_blob(block);
    //result = currency::get_block_hash(block);
    *outputSize = (int) result.length();

    // output buffer big enough?
    if (result.length() > originalOutputSize)
        return false;

    // success
    memcpy(output, result.data(), result.length());
    return true;
}

extern "C" MODULE_API bool get_block_id_export(const char* input, unsigned int inputSize, unsigned char *output)
{
    blobdata input_blob = std::string(input, inputSize);
    crypto::hash block_id;

    currency::block block = AUTO_VAL_INIT(block);
    if (!currency::parse_and_validate_block_from_blob(input_blob, block))
        return false;

    if (!currency::get_block_hash(block, block_id))
        return false;

    char *cstr = reinterpret_cast<char *>(&block_id);

    // success
    memcpy(output, cstr, 32);
    return true;
}
