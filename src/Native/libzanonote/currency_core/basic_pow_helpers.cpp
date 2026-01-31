// Copyright (c) 2018-2019 Zano Project
// Copyright (c) 2018-2019 Hyle Team
// Distributed under the MIT/X11 software license, see the accompanying
// file COPYING or http://www.opensource.org/licenses/mit-license.php.


#include "include_base_utils.h"
using namespace epee;

#include "basic_pow_helpers.h"
#include "currency_format_utils.h"
#include "serialization/binary_utils.h"
#include "serialization/stl_containers.h"
#include "currency_config.h"
#include "crypto/crypto.h"
#include "crypto/hash.h"
#include "common/int-util.h"
//#include "ethereum/libethash/ethash/ethash.hpp"
//#include "ethereum/libethash/ethash/progpow.hpp"

namespace currency
{
  //---------------------------------------------------------------
  crypto::hash get_block_header_mining_hash(const block& b)
  {
    blobdata bd = get_block_hashing_blob(b);

    access_nonce_in_block_blob(bd) = 0;
    return crypto::cn_fast_hash(bd.data(), bd.size());
  }
  //---------------------------------------------------------------
    /*
    Since etherium hash has a bit different approach in minig, to adopt our code we made little hack:
    etherium hash calculates from block's hash and(!) nonce, both passed into PoW hash function.
    To achieve the same effect we make blob of data from block in normal way, but then set to zerro nonce
    inside serialized buffer, and then pass this nonce to ethash algo as a second argument, as it expected.
    */
}