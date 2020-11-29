#include "pch.h"
#pragma once
#pragma managed(push, on)

namespace Tedd {
	namespace OpenVDB {

#pragma once

#include <memory>

		template <class T>
		public ref class native_shared_ptr sealed
		{
			std::shared_ptr<T>* pPtr;

		public:
			native_shared_ptr()
				: pPtr(new std::shared_ptr<T>())
			{}

			native_shared_ptr(T* t) {
				pPtr = new std::shared_ptr<T>(t);
			}

			native_shared_ptr(std::shared_ptr<T> t) {
				pPtr = new std::shared_ptr<T>(t);
			}

			native_shared_ptr(const native_shared_ptr<T>% t) {
				pPtr = new std::shared_ptr<T>(*t.pPtr);
			}

			!native_shared_ptr() {
				delete pPtr;
			}

			~native_shared_ptr() {
				delete pPtr;
			}

			operator std::shared_ptr<T>() {
				return *pPtr;
			}

			native_shared_ptr<T>% operator=(T* ptr) {
				delete pPtr;
				pPtr = new std::shared_ptr<T>(ptr);
				return *this;
			}

			T* operator->() {
				return (*pPtr).get();
			}

			void reset() {
				pPtr->reset();
			}

			void operator=(const native_shared_ptr<T>% rhs)
			{
				*pPtr = *rhs.pPtr;
			}
			void operator=(const std::shared_ptr<T>% rhs)
			{
				*pPtr = rhs;
			}

			std::shared_ptr<T> getSharedPtr()
			{
				return *pPtr;
			}
		};

		//template <class T>
		//public ref class native_shared_ptr sealed
		//{
		//public:
		//    native_shared_ptr()
		//    {
		//        _spNative = new std::shared_ptr<T>;
		//    }
		//    //native_shared_ptr(T* o)
		//    //{
		//    //    _spNative = new std::shared_ptr<T>(o);
		//    //}
		//    native_shared_ptr(const native_shared_ptr<T>% rhs)
		//    {
		//        _spNative = new std::shared_ptr<T>;
		//        *this = rhs;
		//    }
		//    native_shared_ptr(const std::shared_ptr<T>% rhs)
		//    {
		//        _spNative = new std::shared_ptr<T>(rhs);
		//    }
		//    ~native_shared_ptr()
		//    {
		//        reset();
		//    }
		//    void reset()
		//    {
		//        delete _spNative;
		//        _spNative = nullptr;
		//    }
		//    T* operator->()
		//    {
		//        return _spNative->get();
		//    }
		//    operator bool()
		//    {
		//        return _spNative && *_spNative;
		//    }
		//    void operator=(const native_shared_ptr<T>% rhs)
		//    {
		//        *_spNative = *rhs._spNative;
		//    }
		//    void operator=(const std::shared_ptr<T>% rhs)
		//    {
		//        *_spNative = rhs;
		//    }
		//protected:
		//    !native_shared_ptr()
		//    {
		//        reset();
		//    }
		//private:
		//    std::shared_ptr<T>* _spNative;
		//};
	}
}
#pragma managed(pop)